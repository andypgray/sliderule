using Microsoft.CodeAnalysis;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Internal;
using SlideRule.Roslyn.Caching;
using SlideRule.Roslyn.Threading;

namespace SlideRule.Roslyn.Sessions;

/// <summary>
///     What one <see cref="SessionFragmentStore.GetFragmentsAsync" /> call produced: the full fragment set
///     for every C# project — in the ordinal-project-name order a cold run yields, so the downstream
///     <see cref="FragmentMerger" /> is byte-identical — plus the names of the projects this call actually
///     re-walked, empty in the steady state.
/// </summary>
/// <remarks>
///     <paramref name="Version" /> is the store's fragment-set version, bumped only when the stored fragments
///     actually change, so two sets sharing a version hold the identical fragment instances.
/// </remarks>
internal readonly record struct SessionFragmentSet(
    IReadOnlyList<CodebaseFragment> Fragments,
    IReadOnlySet<string> ReExtractedProjects,
    long Version);

/// <summary>
///     What one <see cref="SessionFragmentStore.GetCodebaseAsync" /> call produced: the merged
///     <see cref="CodebaseModel" />, possibly the instance an earlier call built, plus the names of the projects
///     that call re-walked.
/// </summary>
internal readonly record struct SessionCodebase(
    CodebaseModel Model,
    IReadOnlySet<string> ReExtractedProjects);

/// <summary>
///     A session-lifetime, incremental fragment store. It holds the last-extracted
///     <see cref="CodebaseFragment" />s keyed by project name and, on each <see cref="GetFragmentsAsync" />,
///     reuses the clean projects' fragments, re-extracts or remaps only what changed, and returns the whole set
///     for the one <see cref="FragmentMerger" /> every extraction path terminates in. One store pairs with one
///     <see cref="WorkspaceSession" /> for its whole lifetime.
/// </summary>
/// <remarks>
///     <para>
///         A snapshot from a new <see cref="WorkspaceSnapshot.Generation" />, or the very first call, flushes the
///         store and re-extracts every C# project; within a generation, the snapshot's
///         <see cref="WorkspaceSnapshot.ProjectEditVersions" /> pinpoint the dirty projects. The store assumes
///         every snapshot it is handed comes from its one paired session, so a generation value is never aliased
///         by a foreign session's.
///     </para>
///     <para>
///         A change to project P's declaration surface (<see cref="SourceShape.SurfaceHash" />) dirties P plus
///         every project that transitively references P, because it can alter what a dependent compilation binds
///         to or sees as an external type — the same rule the persisted extraction cache folds into its Merkle
///         keys. A body-only edit dirties P alone: a dependent reads P only through the declarations it binds to,
///         never through P's syntax or lines, so its fragment cannot move.
///     </para>
///     <para>
///         A content-dirty project is re-walked only when one of its changed documents changed <em>shape</em>
///         (<see cref="SourceShape" />): when every changed document is shape-equal to the text its fragments were
///         extracted from and yields a line map, the stored fragments keep every fact and have their sites moved
///         through the maps (<see cref="FragmentSiteRemapper" />) instead, each fragment through those of the
///         framework it was extracted under. That is sound because a fragment's facts are read from tokens and
///         directives and never from comments or whitespace, and every site is a token-start line. Dependents of a
///         remapped project are left alone: their compilations see the same metadata, and their sites are in their
///         own files. Any doubt (a split line, two maps for one path under one framework that disagree, a shape
///         that changed) puts the project back on the re-walk path, which is always correct; and since none of
///         those moves its surface, it re-walks alone.
///     </para>
///     <para>
///         The store holds and returns fragments for every C# project, the spec project included; the exclusion
///         is applied at merge time, by <see cref="GetCodebaseAsync" />, from the set its caller passes. Calls may
///         overlap and extraction is asynchronous (a plain lock cannot span the <c>await</c>), so a
///         <see cref="SemaphoreSlim" /> serializes the whole compare-and-extract: a read never races a
///         re-extraction, and the returned set is always internally consistent.
///     </para>
/// </remarks>
internal sealed class SessionFragmentStore : IDisposable
{
    private static readonly IReadOnlySet<string> NoProjects = new HashSet<string>(StringComparer.Ordinal);

    // Keyed by project name; a LIST per name because a multi-target-framework project surfaces as several
    // Projects under the one name the load boundary normalized them to, and therefore several fragments. Each
    // list preserves the cold within-name order — ExtractFragmentsAsync orders those fragments by target
    // framework — which OrderedFragments relies on to reproduce cold order.
    private readonly Dictionary<string, List<CodebaseFragment>> fragmentsByProject = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim gate = new(1, 1);

    // The merged models for the fragment-set version in mergedVersion, keyed by exclusion set. Guarded by
    // its own lock rather than the extraction gate: a merge is CPU-bound and takes no snapshot, so it has no
    // business holding up the compare-and-extract.
    private readonly Dictionary<string, CodebaseModel> merged = new(StringComparer.Ordinal);

    private readonly Lock mergedGate = new();

    // The generation the stored fragments were extracted at. -1 means nothing extracted yet; a loaded
    // snapshot's generation is always >= 1, so the first call always mismatches and full-extracts.
    private long extractedGeneration = -1;

    // The solution the stored fragments were extracted from, which the incremental walk compares each dirty
    // project's changed documents against. Null only before the first walk, which the incremental path never
    // runs before: the first call always mismatches the generation.
    private Solution? extractedSolution;

    // The edit-version map the stored fragments were extracted at, for same-generation dirty diffing.
    private IReadOnlyDictionary<string, int> extractedVersions = new Dictionary<string, int>(StringComparer.Ordinal);

    // The version SessionFragmentSet.Version reports.
    private long fragmentSetVersion;

    // The fragment-set version the memo above holds models for; -1 until the first merge.
    private long mergedVersion = -1;

    // The ordered whole set for the version in orderedVersion, and that version (-1 until the first walk), so a
    // steady-state call re-sorts nothing. Both fields are only ever touched under the extraction gate.
    private IReadOnlyList<CodebaseFragment>? orderedFragments;
    private long orderedVersion = -1;

    /// <summary>
    ///     The project names the last <see cref="GetFragmentsAsync" /> re-walked, a test observable: every C#
    ///     project on a full walk; on an incremental one, the shape-changed projects and the dependents of those
    ///     whose surface moved.
    /// </summary>
    internal IReadOnlySet<string> LastReExtractedProjects { get; private set; } = NoProjects;

    /// <summary>
    ///     The project names the last <see cref="GetFragmentsAsync" /> moved sites for without re-walking — the
    ///     content-dirty projects whose every changed document kept its shape; a test observable.
    /// </summary>
    internal IReadOnlySet<string> LastRemappedProjects { get; private set; } = NoProjects;

    /// <summary>The full walks, the first call and every generation change; a test observable.</summary>
    internal long FullWalkCount { get; private set; }

    public void Dispose()
    {
        gate.Dispose();
    }

    /// <summary>Returns the full fragment set for <paramref name="snapshot" />, reusing everything it can.</summary>
    /// <remarks>
    ///     <paramref name="declaredMembers" /> rides down to the extraction rather than being applied to the
    ///     stored set, and a reused fragment therefore keeps the membership it was extracted with. That is
    ///     safe because membership can only change when the solution file does, and the solution file is a
    ///     structural input of the paired session's reconcile sweep — so the change arrives as a new
    ///     generation, which flushes this store before any of it is read again. Under a solution filter it is
    ///     not: the solution the filter references is no input of that sweep.
    /// </remarks>
    internal async Task<SessionFragmentSet> GetFragmentsAsync(
        WorkspaceSnapshot snapshot, IReadOnlySet<string>? declaredMembers, CancellationToken ct)
    {
        using SemaphoreRelease held = await gate.LockAsync(ct).ConfigureAwait(false);
        return snapshot.Generation != extractedGeneration
            ? await FullWalkAsync(snapshot, declaredMembers, ct).ConfigureAwait(false)
            : await IncrementalWalkAsync(snapshot, declaredMembers, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     The merged codebase for <paramref name="snapshot" />, dropping
    ///     <paramref name="excludeProjectNames" /> at merge time, memoized so a call that re-walked nothing hands
    ///     back the model its predecessor built.
    /// </summary>
    /// <remarks>
    ///     The memo is keyed by the fragment-set version <em>and</em> the exclusion set, so it can never
    ///     serve a model built from other inputs; a version change empties it. Two overlapping calls
    ///     can therefore both merge (the loser's entry is re-made), which costs a merge and never a wrong
    ///     model. Handing back the same instance is safe because a merged model is read-only once built: the
    ///     merge is the only writer of a <c>TypeNode</c>'s hierarchy and members.
    /// </remarks>
    internal async Task<SessionCodebase> GetCodebaseAsync(
        WorkspaceSnapshot snapshot,
        IReadOnlyCollection<string> excludeProjectNames,
        IReadOnlySet<string>? declaredMembers,
        CancellationToken ct)
    {
        SessionFragmentSet set = await GetFragmentsAsync(snapshot, declaredMembers, ct).ConfigureAwait(false);
        return new SessionCodebase(Merge(set, excludeProjectNames), set.ReExtractedProjects);
    }

    private CodebaseModel Merge(SessionFragmentSet set, IReadOnlyCollection<string> excludeProjectNames)
    {
        string key = FragmentMerger.ExclusionKey(excludeProjectNames);
        lock (mergedGate)
        {
            if (mergedVersion != set.Version)
            {
                merged.Clear();
                mergedVersion = set.Version;
            }
            else if (merged.TryGetValue(key, out CodebaseModel? hit))
            {
                return hit;
            }

            CodebaseModel model = FragmentMerger.Merge(FragmentMerger.Retain(set.Fragments, excludeProjectNames));
            merged[key] = model;
            return model;
        }
    }

    private async Task<SessionFragmentSet> FullWalkAsync(
        WorkspaceSnapshot snapshot, IReadOnlySet<string>? declaredMembers, CancellationToken ct)
    {
        IReadOnlyList<CodebaseFragment> fragments = await CodebaseExtractor
            .ExtractFragmentsAsync(snapshot.Solution, null, snapshot.TargetFrameworks, declaredMembers, ct)
            .ConfigureAwait(false);

        fragmentsByProject.Clear();
        Index(fragments);
        extractedGeneration = snapshot.Generation;
        extractedSolution = snapshot.Solution;
        extractedVersions = Copy(snapshot.ProjectEditVersions);
        fragmentSetVersion++;
        FullWalkCount++;
        LastReExtractedProjects = fragments.Select(f => f.ProjectName).ToHashSet(StringComparer.Ordinal);
        LastRemappedProjects = NoProjects;
        return new SessionFragmentSet(OrderedFragments(), LastReExtractedProjects, fragmentSetVersion);
    }

    // A remap lands only once the extraction has returned, so a cancelled extraction leaves every stored fragment
    // describing extractedSolution, the text the next call classifies its edits against.
    private async Task<SessionFragmentSet> IncrementalWalkAsync(
        WorkspaceSnapshot snapshot, IReadOnlySet<string>? declaredMembers, CancellationToken ct)
    {
        HashSet<string> contentDirty = ContentDirtyProjects(snapshot);
        var seed = new HashSet<string>(StringComparer.Ordinal);
        var walkAlone = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new Dictionary<string, List<CodebaseFragment>>(StringComparer.Ordinal);
        if (contentDirty.Count > 0)
        {
            Dictionary<string, ProjectEdit> edits = await ClassifyEditsAsync(snapshot, contentDirty, ct).ConfigureAwait(false);

            foreach (string name in contentDirty)
            {
                if (!edits.TryGetValue(name, out ProjectEdit? edit))
                {
                    seed.Add(name);
                    continue;
                }

                if (edit.MapsByFramework is { } mapsByFramework && RemapStored(name, mapsByFramework) is { } moved)
                    candidates[name] = moved;
                else if (edit.SurfaceHeld)
                    walkAlone.Add(name);
                else
                    seed.Add(name);
            }
        }

        HashSet<string> dirty = ExpandToDependents(seed, snapshot.Solution);
        dirty.UnionWith(walkAlone);
        var remapped = new HashSet<string>(StringComparer.Ordinal);
        var staged = new List<(string Name, List<CodebaseFragment> Moved)>();

        // A candidate the expansion pulled in through a dependent edge is re-walked instead and its remap
        // discarded: the walk answers for both reasons it is dirty, the remap for only one of them.
        foreach ((string name, List<CodebaseFragment> moved) in candidates)
        {
            if (dirty.Contains(name)) continue;

            staged.Add((name, moved));
            remapped.Add(name);
        }

        if (dirty.Count > 0)
        {
            IReadOnlyList<CodebaseFragment> reExtracted = await CodebaseExtractor
                .ExtractFragmentsAsync(snapshot.Solution, dirty, snapshot.TargetFrameworks, declaredMembers, ct)
                .ConfigureAwait(false);

            // Replace only the re-extracted names' lists; the clean projects' fragments ride through untouched.
            foreach (string name in dirty) fragmentsByProject.Remove(name);
            Index(reExtracted);
        }

        foreach ((string name, List<CodebaseFragment> moved) in staged) fragmentsByProject[name] = moved;

        if (dirty.Count > 0 || remapped.Count > 0) fragmentSetVersion++;

        extractedSolution = snapshot.Solution;
        extractedVersions = Copy(snapshot.ProjectEditVersions);
        LastReExtractedProjects = dirty;
        LastRemappedProjects = remapped;
        return new SessionFragmentSet(OrderedFragments(), dirty, fragmentSetVersion);
    }

    /// <summary>
    ///     Each content-dirty project's classified edits, keyed by project name. A dirty name absent from the
    ///     result, one the solution reports no change under, is one the caller must re-walk with its dependents.
    /// </summary>
    /// <remarks>
    ///     The comparison is against <see cref="extractedSolution" /> rather than against the previous call's
    ///     snapshot, so a project remapped on one call is classified on the next against the text its stored
    ///     fragments now describe — successive trivia edits each cost one remap rather than compounding.
    /// </remarks>
    private async Task<Dictionary<string, ProjectEdit>> ClassifyEditsAsync(
        WorkspaceSnapshot snapshot, HashSet<string> contentDirty, CancellationToken ct)
    {
        var edits = new Dictionary<string, ProjectEdit>(StringComparer.Ordinal);
        if (extractedSolution is not { } extracted) return edits;

        SolutionChanges changes = snapshot.Solution.GetChanges(extracted);
        IEnumerable<IGrouping<string, ProjectChanges>> byName = changes.GetProjectChanges()
            .GroupBy(change => change.NewProject.Name, StringComparer.Ordinal);

        foreach (IGrouping<string, ProjectChanges> group in byName)
        {
            if (!contentDirty.Contains(group.Key)) continue;

            edits[group.Key] = await ClassifyProjectAsync(group, snapshot.TargetFrameworks, ct).ConfigureAwait(false);
        }

        return edits;
    }

    /// <summary>
    ///     One project name's edits across every <see cref="ProjectChanges" /> it carries — several where a
    ///     multi-target-framework project's frameworks each compile the file: its maps, keyed by target
    ///     framework and then by path, or <see langword="null" /> where any of them says re-walk; and whether
    ///     its declaration surface held.
    /// </summary>
    /// <remarks>
    ///     Each framework keeps its own maps because a map is a reading of one compilation's parse: inside a
    ///     framework-conditional region one framework sees code and another disabled text, so their maps for
    ///     one path differ and each is right about its own compilation. Two maps filed under one framework for
    ///     one path must still agree. That happens where the load split a project without recording its
    ///     frameworks, and there a disagreement cannot be told apart from a shape read that cannot be trusted
    ///     to move a site, so it is refused rather than resolved. Paths are keyed by
    ///     <see cref="PathComparison.Comparer" /> because a site spells the tree's own file path. The surface
    ///     verdict is the conservative one: an added or removed document, or any change to an additional or
    ///     analyzer-config document, which a generator can read, counts as a surface change.
    /// </remarks>
    private static async Task<ProjectEdit> ClassifyProjectAsync(
        IEnumerable<ProjectChanges> group,
        IReadOnlyDictionary<ProjectId, string> targetFrameworks,
        CancellationToken ct)
    {
        var mapsByFramework = new Dictionary<string, Dictionary<string, LineMap>>(StringComparer.Ordinal);
        var mappable = true;
        foreach (ProjectChanges change in group)
        {
            if (change.GetAddedDocuments().Any() || change.GetRemovedDocuments().Any() || TouchesNonSourceDocuments(change))
                return new ProjectEdit(null, SurfaceHeld: false);

            string framework = FrameworkKey(targetFrameworks.GetValueOrDefault(change.NewProject.Id));
            if (!mapsByFramework.TryGetValue(framework, out Dictionary<string, LineMap>? maps))
            {
                maps = new Dictionary<string, LineMap>(PathComparison.Comparer);
                mapsByFramework[framework] = maps;
            }

            foreach (DocumentId id in change.GetChangedDocuments())
            {
                Document? before = change.OldProject.GetDocument(id);
                Document? after = change.NewProject.GetDocument(id);
                if (before is null || after is null) return new ProjectEdit(null, SurfaceHeld: false);

                SyntaxTree? beforeTree = await before.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
                SyntaxTree? afterTree = await after.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
                if (beforeTree is null || afterTree is null) return new ProjectEdit(null, SurfaceHeld: false);

                SourceShape beforeShape = SourceShape.Of(beforeTree);
                SourceShape afterShape = SourceShape.Of(afterTree);
                if (!string.Equals(beforeShape.SurfaceHash, afterShape.SurfaceHash, StringComparison.Ordinal))
                    return new ProjectEdit(null, SurfaceHeld: false);

                if (!mappable) continue;

                LineMap? map = SourceShape.TryMapLines(beforeShape, afterShape);
                string path = afterTree.FilePath;
                if (map is null || (maps.TryGetValue(path, out LineMap? agreed) && !agreed.Equals(map)))
                {
                    mappable = false;
                    continue;
                }

                maps[path] = map;
            }
        }

        return new ProjectEdit(mappable ? mapsByFramework : null, SurfaceHeld: true);
    }

    private static bool TouchesNonSourceDocuments(ProjectChanges change)
    {
        return change.GetAddedAdditionalDocuments().Any()
               || change.GetRemovedAdditionalDocuments().Any()
               || change.GetChangedAdditionalDocuments().Any()
               || change.GetAddedAnalyzerConfigDocuments().Any()
               || change.GetRemovedAnalyzerConfigDocuments().Any()
               || change.GetChangedAnalyzerConfigDocuments().Any();
    }

    /// <summary>
    ///     One content-dirty project's classified edits. <see cref="MapsByFramework" /> is non-null only where
    ///     every changed document was trivia-only, and then <see cref="SurfaceHeld" /> is true too, since no
    ///     surface sees trivia.
    /// </summary>
    private sealed record ProjectEdit(
        Dictionary<string, Dictionary<string, LineMap>>? MapsByFramework,
        bool SurfaceHeld);

    // One project's stored fragments, each with every site moved through the maps of the framework it was
    // extracted under, or null where any fragment carries a site those maps cannot place — the remapper's own
    // signal to re-walk. A fragment whose framework has no maps was compiled from no changed document, so it
    // rides through as it is. A name the store holds no fragments for reads as a refusal, so a dirty project
    // that was never extracted is walked rather than silently left empty.
    private List<CodebaseFragment>? RemapStored(
        string name, IReadOnlyDictionary<string, Dictionary<string, LineMap>> mapsByFramework)
    {
        if (!fragmentsByProject.TryGetValue(name, out List<CodebaseFragment>? stored)) return null;

        var moved = new List<CodebaseFragment>(stored.Count);
        foreach (CodebaseFragment fragment in stored)
        {
            string framework = FrameworkKey(fragment.TargetFramework);
            if (!mapsByFramework.TryGetValue(framework, out Dictionary<string, LineMap>? maps))
            {
                moved.Add(fragment);
                continue;
            }

            CodebaseFragment? remapped = FragmentSiteRemapper.TryRemap(fragment, maps);
            if (remapped is null) return null;

            moved.Add(remapped);
        }

        return moved;
    }

    // The key a framework's maps file under. A single-framework project records no framework, and neither
    // does a load that split a project without telling its frameworks apart; both file under the empty key,
    // which no framework moniker spells.
    private static string FrameworkKey(string? targetFramework)
    {
        return targetFramework ?? "";
    }

    // Projects whose edit version differs from the one the stored fragments were extracted at. Within a
    // generation both maps share a fixed key set (seeded at load), so a key is never actually missing; the
    // -1 default is a defensive "treat as dirty" for the impossible mismatch.
    private HashSet<string> ContentDirtyProjects(WorkspaceSnapshot snapshot)
    {
        var dirty = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string name, int version) in snapshot.ProjectEditVersions)
            if (extractedVersions.GetValueOrDefault(name, -1) != version)
                dirty.Add(name);
        return dirty;
    }

    private static HashSet<string> ExpandToDependents(HashSet<string> seed, Solution solution)
    {
        if (seed.Count == 0) return seed;

        Dictionary<string, HashSet<string>> dependentsByName = BuildReverseReferenceGraph(solution);
        var result = new HashSet<string>(seed, StringComparer.Ordinal);
        var queue = new Queue<string>(seed);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (!dependentsByName.TryGetValue(current, out HashSet<string>? dependents)) continue;

            foreach (string dependent in dependents)
                if (result.Add(dependent))
                    queue.Enqueue(dependent);
        }

        return result;
    }

    private static Dictionary<string, HashSet<string>> BuildReverseReferenceGraph(Solution solution)
    {
        var dependentsByName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Project project in solution.Projects)
        {
            if (project.Language != LanguageNames.CSharp) continue;

            foreach (ProjectReference reference in project.ProjectReferences)
            {
                if (solution.GetProject(reference.ProjectId)?.Name is not { } referenced) continue;

                if (!dependentsByName.TryGetValue(referenced, out HashSet<string>? dependents))
                {
                    dependents = new HashSet<string>(StringComparer.Ordinal);
                    dependentsByName[referenced] = dependents;
                }

                dependents.Add(project.Name);
            }
        }

        return dependentsByName;
    }

    private void Index(IReadOnlyList<CodebaseFragment> fragments)
    {
        foreach (CodebaseFragment fragment in fragments)
        {
            if (!fragmentsByProject.TryGetValue(fragment.ProjectName, out List<CodebaseFragment>? list))
            {
                list = [];
                fragmentsByProject[fragment.ProjectName] = list;
            }

            list.Add(fragment);
        }
    }

    // The whole stored set in ordinal-project-name order. OrderBy is a stable sort, so within a name the
    // fragments keep their stored (cold) order — reproducing a cold run's fragment order exactly, and with
    // it the merged model byte for byte. Sharing the cached list is safe for the reason the shared model is:
    // a fragment is immutable data and every consumer reads.
    private IReadOnlyList<CodebaseFragment> OrderedFragments()
    {
        if (orderedVersion == fragmentSetVersion && orderedFragments is { } cached) return cached;

        List<CodebaseFragment> ordered = fragmentsByProject.Values
            .SelectMany(list => list)
            .OrderBy(f => f.ProjectName, StringComparer.Ordinal)
            .ToList();

        orderedFragments = ordered;
        orderedVersion = fragmentSetVersion;
        return ordered;
    }

    private static IReadOnlyDictionary<string, int> Copy(IReadOnlyDictionary<string, int> versions)
    {
        return new Dictionary<string, int>(versions, StringComparer.Ordinal);
    }
}
