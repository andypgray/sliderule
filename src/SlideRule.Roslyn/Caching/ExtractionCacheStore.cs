using System.Security.Cryptography;
using System.Text;
using SlideRule.Extraction.Fragments;
using SlideRule.Internal;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Caching;

/// <summary>The outcome of validating a persisted extraction cache against disk.</summary>
internal enum CacheOutcome
{
    /// <summary>Every project is clean: the whole model can be rebuilt from cached fragments, no workspace.</summary>
    Hit,

    /// <summary>
    ///     Some projects are dirty: reuse the clean fragments, re-extract the
    ///     <see cref="CacheReadResult.DirtyProjects" />.
    /// </summary>
    Partial,

    /// <summary>Unusable (missing/garbled/version-mismatch/structural change): fall back to the full cold path.</summary>
    Miss
}

/// <summary>
///     The result of <see cref="ExtractionCacheStore.ReadAndValidate" />: what to reuse and what to redo, with
///     everything a caller needs to finish a run without re-reading the cache.
/// </summary>
/// <remarks>
///     The recorded spec resolutions and the recorded load verdict are replayed on a hit, so cached and cold
///     output, and the fail-closed decision, are identical on a solution that does not load completely or did
///     not restore. <paramref name="Validated" />, set on a <see cref="CacheOutcome.Partial" /> read only, hands
///     <see cref="ExtractionCacheStore.TryCaptureOverValidation" /> the stamps and keys the validation already
///     took.
/// </remarks>
internal sealed record CacheReadResult(
    CacheOutcome Outcome,
    IReadOnlyList<CodebaseFragment> ReusableFragments,
    IReadOnlySet<string> DirtyProjects,
    IReadOnlyList<SpecResolutionRecord> SpecResolutions,
    WorkspaceDiagnostics LoadDiagnostics,
    ValidatedStamps? Validated = null)
{
    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();

    internal static CacheReadResult Miss()
    {
        return new CacheReadResult(CacheOutcome.Miss, [], EmptySet, [], WorkspaceDiagnostics.None);
    }

    internal static CacheReadResult Hit(
        IReadOnlyList<CodebaseFragment> fragments,
        IReadOnlyList<SpecResolutionRecord> specResolutions,
        WorkspaceDiagnostics loadDiagnostics)
    {
        return new CacheReadResult(CacheOutcome.Hit, fragments, EmptySet, specResolutions, loadDiagnostics);
    }

    internal static CacheReadResult Partial(
        IReadOnlyList<CodebaseFragment> reusableFragments,
        IReadOnlySet<string> dirtyProjects,
        IReadOnlyList<SpecResolutionRecord> specResolutions,
        WorkspaceDiagnostics loadDiagnostics,
        ValidatedStamps validated)
    {
        return new CacheReadResult(
            CacheOutcome.Partial, reusableFragments, dirtyProjects, specResolutions, loadDiagnostics, validated);
    }
}

/// <summary>
///     What a <see cref="CacheOutcome.Partial" /> validation already knows about disk, handed on so the capture
///     that follows the re-extraction reads only what changed rather than every document of the solution.
/// </summary>
/// <remarks>
///     A partial is never a structural change: any change to the solution, a project file, an assets file or a
///     probe file is a miss. So the structural stamps, the project set and the reference graph a partial
///     validated are the ones the capture would take again, and a clean project's documents, true content key
///     and surface key are exactly what a full re-stamp would recompute. Both lists are in manifest order, and
///     every project outside <paramref name="DirtyProjects" /> is carried whole.
/// </remarks>
internal sealed record ValidatedStamps(
    IReadOnlyList<FileStamp> StructuralStamps,
    IReadOnlyList<ValidatedProject> Projects,
    IReadOnlySet<string> DirtyProjects)
{
    /// <summary>The project files of <see cref="DirtyProjects" />: what a run that re-extracts them must load.</summary>
    internal IReadOnlyList<string> DirtyProjectFiles =>
        Projects
            .Where(project => DirtyProjects.Contains(project.Entry.ProjectName))
            .Select(project => project.Entry.CsprojPath)
            .Distinct(PathComparison.Comparer)
            .ToList();
}

/// <summary>
///     One manifest project as a <see cref="CacheOutcome.Partial" /> validation left it: <paramref name="Entry" />
///     carries the refreshed document stamps and the true content and surface keys but the recorded Merkle key,
///     which a capture recomputes, and <paramref name="UnchangedDocuments" /> holds, by path, the refreshed stamps
///     of the documents whose bytes validation found unchanged.
/// </summary>
internal sealed record ValidatedProject(
    ProjectCacheEntry Entry,
    IReadOnlyDictionary<string, FileStamp> UnchangedDocuments);

/// <summary>
///     One project's identity as the caller knows it before any file is statted — the store fills in every
///     stamp and key itself, so the hashing lives in exactly one place and read/write agree by construction.
/// </summary>
/// <remarks>
///     <paramref name="ProjectReferences" /> is the compiler's closure, as on <see cref="ProjectCacheEntry" />.
///     <paramref name="EvaluatedOutputPath" /> and <paramref name="IntermediateAssemblyPath" />, null when the
///     workspace carried none, are the pair <see cref="IntermediateOutputTree.AssetsPathsOf" /> reads.
/// </remarks>
internal sealed record ProjectInputs(
    string ProjectName,
    string CsprojPath,
    string ProjectDirectory,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> DocumentPaths,
    string? EvaluatedOutputPath = null,
    string? IntermediateAssemblyPath = null);

/// <summary>
///     A pre-extraction fingerprint of the workspace's files: the structural stamps and per-project entries, as
///     the manifest will record them, taken before extraction started and handed back to
///     <see cref="ExtractionCacheStore.Write" />.
/// </summary>
/// <remarks>
///     <paramref name="FreshStamps" /> holds the stamps a capture over a partial validation took from disk, the
///     only ones <see cref="ExtractionCacheStore.Write" /> re-stats; null when the capture stamped everything.
/// </remarks>
internal sealed record CacheFingerprint(
    IReadOnlyList<FileStamp> StructuralStamps,
    IReadOnlyList<ProjectCacheEntry> Projects,
    IReadOnlyList<FileStamp>? FreshStamps = null);

/// <summary>
///     What a workspace-loaded run produced and wants persisted. The manifest stores the load verdict's lists
///     but not its merge notes or multi-targeted projects, which every path regenerates from the fragments.
/// </summary>
internal sealed record ExtractionResult(
    IReadOnlyList<CodebaseFragment> Fragments,
    IReadOnlyList<SpecResolutionRecord> SpecResolutions,
    WorkspaceDiagnostics LoadDiagnostics);

/// <summary>
///     The read/validate/write boundary over one solution's persisted extraction cache, a single atomic
///     <c>cache.json</c> holding the manifest and every fragment. Validation runs with zero MSBuild.
/// </summary>
/// <remarks>
///     <para>
///         Racy-window and structural semantics follow the warm <see cref="WorkspaceSession" />'s reconcile
///         sweep, with one refinement: a structural file is content-verified, so a bare mtime touch on a csproj
///         whose bytes are unchanged is not a miss, because a false miss here costs a full cold rebuild.
///     </para>
///     <para>
///         A trivia-only edit is a hit, not a partial: where a document's bytes changed but its
///         <see cref="SourceShape" /> did not, validation keys the dirty decision on the recorded hash and
///         replays the project's fragments with their sites moved through the line map
///         (<see cref="FragmentSiteRemapper" />).
///     </para>
///     <para>
///         A body-only edit dirties its own project and no dependent: a dependent's Merkle key folds in its
///         dependencies' <em>surface</em> keys (<see cref="SourceShape.SurfaceHash" />), not their content
///         keys, because a dependent's fragment reads a referenced project only through the declarations it
///         binds to. A project's own Merkle key still folds in its own content key, so it re-walks on any
///         token edit of its own.
///     </para>
/// </remarks>
internal sealed class ExtractionCacheStore
{
    // The on-disk schema of this per-solution cache file. A record written under any other version degrades to
    // a clean Miss and is rebuilt. Bump this whenever a fragment gains a fact, OR changes how one is computed:
    // a widened fact keeps its name and its type while taking a different value for identical inputs, so a hit
    // would replay the old answer forever with nothing to distinguish it. The same holds when a manifest record
    // gains a slot, even one that deserializes to a harmless default, so a reader never reasons about which
    // slots a file predates. The suite cannot catch a missed bump — every run gets a fresh cache directory — so
    // the discipline is the only guard.
    private const int CurrentSchemaVersion = 39;

    private static readonly IReadOnlySet<string> NoDocuments = new HashSet<string>(PathComparison.Comparer);

    private readonly string cacheFilePath;
    private readonly string solutionPath;
    private long captureStampCount;
    private long contentHashCount;

    /// <summary>
    ///     Creates a store for <paramref name="solutionPath" />'s cache. The file lives under
    ///     <paramref name="cacheRootOverride" /> when given, else the default cache root; either way in the
    ///     per-solution subdirectory <see cref="CacheLocations" /> derives.
    /// </summary>
    public ExtractionCacheStore(string solutionPath, string? cacheRootOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        this.solutionPath = solutionPath;
        cacheFilePath = CacheLocations.CacheFilePath(solutionPath, cacheRootOverride);
    }

    /// <summary>
    ///     The file-content reads (SHA-256 computations) validation has made, a test observable: zero in the
    ///     steady state, where every input is trusted on stat alone. Counted through <see cref="Interlocked" />,
    ///     because the per-project sweep is parallel and the tests that read it pin exact deltas.
    /// </summary>
    internal long ContentHashCount => Volatile.Read(ref contentHashCount);

    /// <summary>
    ///     The absolute paths of the documents the last <see cref="ReadAndValidate" /> moved sites for instead of
    ///     dirtying their project; a test observable.
    /// </summary>
    internal IReadOnlySet<string> LastRemappedDocuments { get; private set; } = NoDocuments;

    /// <summary>
    ///     The documents captures have read from disk to stamp, a test observable counted through
    ///     <see cref="Interlocked" /> as <see cref="ContentHashCount" /> is.
    /// </summary>
    internal long CaptureStampCount => Volatile.Read(ref captureStampCount);

    /// <summary>
    ///     The fingerprint to hand to <see cref="Write" />: the one <see cref="TryCaptureOverValidation" /> builds
    ///     from <paramref name="validated" /> where it can, and otherwise every input stamped now. Does not touch
    ///     <see cref="ContentHashCount" /> (that counts validation reads only) and writes nothing.
    /// </summary>
    public CacheFingerprint CaptureFingerprint(
        IReadOnlyList<ProjectInputs> projects, ValidatedStamps? validated, CancellationToken ct = default)
    {
        return TryCaptureOverValidation(projects, validated, ct) ?? CaptureEverything(projects, ct);
    }

    /// <summary>
    ///     The fingerprint that carries <paramref name="validated" />, or null when it cannot: no validation, or
    ///     <paramref name="projects" /> holds a project the validation does not know or lacks a dirty one. A clean
    ///     project is taken whole from the validation; a dirty project keeps the stamps of the documents validation
    ///     found unchanged and stamps the rest from <paramref name="projects" />, which decide its document list
    ///     and may be only the dirty projects' closure.
    /// </summary>
    /// <remarks>
    ///     A caller whose <paramref name="projects" /> are only a closure must not fall back to stamping them alone,
    ///     because a manifest without the projects it did not load would leave their fragments unvalidated.
    /// </remarks>
    public CacheFingerprint? TryCaptureOverValidation(
        IReadOnlyList<ProjectInputs> projects, ValidatedStamps? validated, CancellationToken ct = default)
    {
        return validated is not null && Covers(validated, projects)
            ? CaptureOverValidation(projects, validated, ct)
            : null;
    }

    // The loaded projects must name every dirty project, so each has a document list to stamp, and nothing the
    // validation does not know, so the written project set stays the manifest's.
    private static bool Covers(ValidatedStamps validated, IReadOnlyList<ProjectInputs> projects)
    {
        HashSet<string> known = validated.Projects.Select(project => project.Entry.ProjectName).ToHashSet(StringComparer.Ordinal);
        HashSet<string> loaded = projects.Select(project => project.ProjectName).ToHashSet(StringComparer.Ordinal);
        return loaded.IsSubsetOf(known) && validated.DirtyProjects.All(loaded.Contains);
    }

    private CacheFingerprint CaptureOverValidation(
        IReadOnlyList<ProjectInputs> projects, ValidatedStamps validated, CancellationToken ct)
    {
        Dictionary<string, string?> structuralShaByPath = BuildStructuralShaLookup(validated.StructuralStamps);

        // Re-keyed by name in input order, so a repeated name resolves to its last occurrence.
        var loadedByName = new Dictionary<string, ProjectInputs>(StringComparer.Ordinal);
        foreach (ProjectInputs project in projects) loadedByName[project.ProjectName] = project;

        int count = validated.Projects.Count;
        var entryByIndex = new ProjectCacheEntry[count];
        var freshByIndex = new IReadOnlyList<FileStamp>[count];

        Parallel.For(0, count, new ParallelOptions { CancellationToken = ct }, index =>
        {
            ValidatedProject carried = validated.Projects[index];
            if (!validated.DirtyProjects.Contains(carried.Entry.ProjectName))
            {
                entryByIndex[index] = carried.Entry;
                freshByIndex[index] = [];
                return;
            }

            (entryByIndex[index], freshByIndex[index]) = RestampDirty(
                loadedByName[carried.Entry.ProjectName], carried.UnchangedDocuments, structuralShaByPath, ct);
        });

        List<ProjectCacheEntry> entries = WithMerkleKeys(entryByIndex);

        return new CacheFingerprint(validated.StructuralStamps, entries, freshByIndex.SelectMany(stamps => stamps).ToList());
    }

    // One dirty project's entry: the loaded document list, each document's validated stamp where its bytes were
    // unchanged and a fresh stamp otherwise, and the keys over them. The Merkle key is left for the caller.
    private (ProjectCacheEntry Entry, IReadOnlyList<FileStamp> Fresh) RestampDirty(
        ProjectInputs project,
        IReadOnlyDictionary<string, FileStamp> unchanged,
        IReadOnlyDictionary<string, string?> structuralShaByPath,
        CancellationToken ct)
    {
        var documents = new List<FileStamp>(project.DocumentPaths.Count);
        var fresh = new List<FileStamp>();
        foreach (string path in project.DocumentPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (unchanged.TryGetValue(Path.GetFullPath(path), out FileStamp? carried))
            {
                documents.Add(carried);
                continue;
            }

            FileStamp stamp = StampDocumentForCapture(path);
            documents.Add(stamp);
            fresh.Add(stamp);
        }

        (string contentKey, string surfaceKey) = ProjectKeysOf(project, documents, structuralShaByPath);
        var entry = new ProjectCacheEntry(
            project.ProjectName,
            Path.GetFullPath(project.CsprojPath),
            Path.GetFullPath(project.ProjectDirectory),
            project.ProjectReferences,
            documents,
            contentKey,
            "",
            surfaceKey);
        return (entry, fresh);
    }

    // A project's content and surface keys over its document stamps, its structural inputs and its cone-adds.
    private static (string ContentKey, string SurfaceKey) ProjectKeysOf(
        ProjectInputs project, IReadOnlyList<FileStamp> documents, IReadOnlyDictionary<string, string?> structuralShaByPath)
    {
        List<(string Path, string? Sha256)> documentShas = documents.Select(d => (d.Path, d.Sha256)).ToList();
        (string? csprojSha, string? assetsSha, IReadOnlyList<string> adds) = KeyInputsOf(
            project.CsprojPath, project.ProjectDirectory, documents, structuralShaByPath);
        return (ComputeContentKey(project.ProjectName, documentShas, csprojSha, assetsSha, adds),
            ComputeContentKey(project.ProjectName, SurfacesOf(documents), csprojSha, assetsSha, adds));
    }

    // The structural shas and cone-adds of a project's content keys. Capture and validation must compute these
    // alike, over the same known-document set (the stamps' full paths), so that a *.cs on disk under the project
    // but excluded from compilation is an add on both sides and cancels, and only a genuine add moves the key. A
    // capture that left adds empty would never validate, and the project would stay dirty forever.
    private static (string? CsprojSha, string? AssetsSha, IReadOnlyList<string> Adds) KeyInputsOf(
        string csprojPath,
        string projectDirectory,
        IReadOnlyList<FileStamp> documents,
        IReadOnlyDictionary<string, string?> structuralShaByPath)
    {
        string? csprojSha = structuralShaByPath.GetValueOrDefault(Path.GetFullPath(csprojPath));
        string? assetsSha = structuralShaByPath.GetValueOrDefault(
            IntermediateOutputTree.DefaultAssetsPathOf(projectDirectory));

        var knownDocuments = new HashSet<string>(documents.Select(d => d.Path), PathComparison.Comparer);
        IReadOnlyList<string> adds = ProjectCone.Adds(Path.GetFullPath(projectDirectory), knownDocuments);
        return (csprojSha, assetsSha, adds);
    }

    private CacheFingerprint CaptureEverything(IReadOnlyList<ProjectInputs> projects, CancellationToken ct)
    {
        List<FileStamp> structuralStamps = ProjectCone.SolutionStructuralPaths(solutionPath, projects)
            .Select(FileStamping.StampOf)
            .ToList();

        Dictionary<string, string?> structuralShaByPath = BuildStructuralShaLookup(structuralStamps);

        // Each project's fingerprint reads only its own files, so the whole hash-and-parse pass over the
        // solution's sources runs in parallel — the one place a cold run spends real wall-clock — and lands
        // in position-indexed slots. The Merkle pass needs every content key before it can start, so it
        // stays sequential.
        var entryByIndex = new ProjectCacheEntry[projects.Count];

        Parallel.For(0, projects.Count, new ParallelOptions { CancellationToken = ct }, index =>
        {
            ProjectInputs project = projects[index];

            IReadOnlyList<FileStamp> documents = StampDocuments(project.DocumentPaths, ct);
            (string contentKey, string surfaceKey) = ProjectKeysOf(project, documents, structuralShaByPath);
            entryByIndex[index] = new ProjectCacheEntry(
                project.ProjectName,
                Path.GetFullPath(project.CsprojPath),
                Path.GetFullPath(project.ProjectDirectory),
                project.ProjectReferences,
                documents,
                contentKey,
                "",
                surfaceKey);
        });

        return new CacheFingerprint(structuralStamps, WithMerkleKeys(entryByIndex));
    }

    // The one Merkle pass every capture runs, setting each entry's Merkle key from the same kinds of key
    // validation recomputes, so capture and validation agree by construction.
    private static List<ProjectCacheEntry> WithMerkleKeys(IReadOnlyList<ProjectCacheEntry> entries)
    {
        var surfaceKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var referencesByName = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (ProjectCacheEntry entry in entries)
        {
            surfaceKeys[entry.ProjectName] = entry.SurfaceKey;
            referencesByName[entry.ProjectName] = entry.ProjectReferences;
        }

        var surfaceMemo = new Dictionary<string, string>(StringComparer.Ordinal);
        return entries
            .Select(entry => entry with
            {
                MerkleKey = ComputeMerkleKey(
                    entry.ContentKey, entry.ProjectName, surfaceKeys, referencesByName, surfaceMemo)
            })
            .ToList();
    }

    /// <summary>
    ///     Commits <paramref name="extraction" /> to the cache, keyed to <paramref name="fingerprint" />.
    ///     Re-stats every input the capture took from disk first; if any changed since
    ///     <see cref="CaptureFingerprint(IReadOnlyList{ProjectInputs}, ValidatedStamps, CancellationToken)" />
    ///     (a mid-run edit), skips the write and returns <c>false</c>. A best-effort I/O failure also returns
    ///     <c>false</c>. Otherwise writes atomically and returns <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     A stamp carried from a partial validation is not re-statted. It was taken before the extraction began,
    ///     so an edit since then leaves it unequal to disk, and the next validation catches that by its stat.
    /// </remarks>
    public bool Write(CacheFingerprint fingerprint, ExtractionResult extraction, CancellationToken ct = default)
    {
        IEnumerable<FileStamp> guarded = fingerprint.FreshStamps
                                         ?? fingerprint.StructuralStamps.Concat(
                                             fingerprint.Projects.SelectMany(project => project.Documents));
        foreach (FileStamp stamp in guarded)
        {
            ct.ThrowIfCancellationRequested();
            if (StatChangedSinceCapture(stamp)) return false;
        }

        var manifest = new CacheManifest(
            CurrentSchemaVersion,
            FileStamping.CurrentToolVersion,
            fingerprint.StructuralStamps,
            fingerprint.Projects,
            extraction.SpecResolutions,
            extraction.LoadDiagnostics.LoadFailures,
            extraction.LoadDiagnostics.FailedProjects,
            extraction.LoadDiagnostics.UncheckedProjects,
            extraction.LoadDiagnostics.RestoreFailedProjects,
            extraction.LoadDiagnostics.UnsupportedProjects,
            extraction.Fragments);

        return TryWriteAtomic(manifest);
    }

    /// <summary>
    ///     Reads and validates the cache against disk with zero MSBuild, returning what to reuse and what to
    ///     re-extract. A hit that re-hashed a settled file rewrites promoted stamps so the next call is
    ///     pure-stat. Never throws (bar cancellation) — any failure is a miss.
    /// </summary>
    public CacheReadResult ReadAndValidate(CancellationToken ct = default)
    {
        try
        {
            return ValidateCore(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any unexpected failure (I/O mid-scan, a malformed record STJ still bound) degrades to a miss:
            // the cache is disposable, so the run falls back to the cold path rather than surfacing an error.
            return CacheReadResult.Miss();
        }
    }

    private CacheReadResult ValidateCore(CancellationToken ct)
    {
        LastRemappedDocuments = NoDocuments;

        CacheManifest? manifest = TryRead();
        if (manifest is null) return CacheReadResult.Miss();
        if (manifest.SchemaVersion != CurrentSchemaVersion) return CacheReadResult.Miss();
        if (!string.Equals(manifest.ToolVersion, FileStamping.CurrentToolVersion, StringComparison.Ordinal)) return CacheReadResult.Miss();

        // Structural sweep — any existence flip or content change is a full miss.
        if (FileStamping.SweepStructural(
                manifest.StructuralStamps, () => Interlocked.Increment(ref contentHashCount), ct, out _)
            is not { } refreshedStructural)
            return CacheReadResult.Miss();

        Dictionary<string, string?> structuralShaByPath = BuildStructuralShaLookup(refreshedStructural);

        // Per-document sweep + cone scan ⇒ each project's recomputed content keys. Independent per project —
        // each reads only its own documents and its own cone — so it runs in parallel into position-indexed
        // slots; the Merkle pass below needs them all and stays sequential.
        var checkedByIndex = new ProjectCheck[manifest.Projects.Count];

        Parallel.For(0, manifest.Projects.Count, new ParallelOptions { CancellationToken = ct },
            index => checkedByIndex[index] = CheckProject(manifest.Projects[index], structuralShaByPath, ct));

        var surfaceKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var referencesByName = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var mapsByProject = new Dictionary<string, IReadOnlyDictionary<string, LineMap>>(StringComparer.Ordinal);
        foreach (ProjectCheck check in checkedByIndex)
        {
            surfaceKeys[check.ProjectName] = check.SurfaceKey;
            referencesByName[check.ProjectName] = check.ProjectReferences;
            if (check.Maps is { } maps) mapsByProject[check.ProjectName] = maps;
        }

        // Recompute Merkle keys over the DECISION keys, in which a trivia-only edit still shows the hash its
        // fragments were extracted from, and the dependencies' surface keys, which no body-only edit moves; the
        // dirty set is exactly the projects whose key no longer matches — content-dirty projects plus every
        // dependent of a project whose declaration surface changed.
        var dirtyProjects = new HashSet<string>(StringComparer.Ordinal);
        var surfaceMemo = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < manifest.Projects.Count; index++)
        {
            ProjectCacheEntry project = manifest.Projects[index];
            string recomputedMerkle = ComputeMerkleKey(
                checkedByIndex[index].DecisionContentKey, project.ProjectName, surfaceKeys, referencesByName,
                surfaceMemo);
            if (!string.Equals(recomputedMerkle, project.MerkleKey, StringComparison.Ordinal))
                dirtyProjects.Add(project.ProjectName);
        }

        IReadOnlyList<CodebaseFragment> replayedFragments = Replay(manifest.Fragments, mapsByProject, dirtyProjects);
        LastRemappedDocuments = RemappedDocumentsOf(mapsByProject, dirtyProjects);

        // The merge's notes and multi-targeted projects are empty: nothing persists them, because a hit re-merges
        // the stored fragments and produces both afresh.
        WorkspaceDiagnostics loadDiagnostics = WorkspaceDiagnostics.OfLoad(
            loadFailures: manifest.Diagnostics,
            failedProjects: manifest.FailedProjects,
            uncheckedProjects: manifest.UncheckedProjects,
            restoreFailedProjects: manifest.RestoreFailedProjects,
            unsupportedProjects: manifest.UnsupportedProjects);

        if (dirtyProjects.Count == 0)
        {
            IReadOnlyList<ProjectCacheEntry> refreshedProjects = RefreshedProjects(
                manifest.Projects, checkedByIndex, surfaceKeys, referencesByName, surfaceMemo);
            PromoteIfChanged(manifest, refreshedStructural, refreshedProjects, replayedFragments);
            return CacheReadResult.Hit(replayedFragments, manifest.SpecResolutions, loadDiagnostics);
        }

        List<CodebaseFragment> reusable = replayedFragments.Where(f => !dirtyProjects.Contains(f.ProjectName)).ToList();
        ValidatedStamps validated = ValidatedStampsOf(manifest.Projects, checkedByIndex, refreshedStructural, dirtyProjects);
        return CacheReadResult.Partial(reusable, dirtyProjects, manifest.SpecResolutions, loadDiagnostics, validated);
    }

    /// <summary>
    ///     What a partial hands the capture: every manifest project with its refreshed stamps and its
    ///     <em>true</em> keys, the same ones a hit promotes, and for a dirty project the documents whose bytes
    ///     did not change.
    /// </summary>
    /// <remarks>
    ///     A document counts as unchanged only when it still exists, its hash matches the recorded one and its
    ///     stamp carries a shape: a stamp with none would give its project a different surface key than a fresh
    ///     stamp of the same bytes, so it is stamped again.
    /// </remarks>
    private static ValidatedStamps ValidatedStampsOf(
        IReadOnlyList<ProjectCacheEntry> projects,
        IReadOnlyList<ProjectCheck> checkedByIndex,
        IReadOnlyList<FileStamp> refreshedStructural,
        IReadOnlySet<string> dirtyProjects)
    {
        var validated = new List<ValidatedProject>(projects.Count);
        for (var index = 0; index < projects.Count; index++)
        {
            ProjectCacheEntry project = projects[index];
            ProjectCheck check = checkedByIndex[index];
            ProjectCacheEntry entry = project with
            {
                Documents = check.RefreshedDocuments, ContentKey = check.TrueContentKey, SurfaceKey = check.SurfaceKey
            };

            var unchanged = new Dictionary<string, FileStamp>(PathComparison.Comparer);
            if (dirtyProjects.Contains(project.ProjectName))
                for (var document = 0; document < project.Documents.Count; document++)
                {
                    FileStamp recorded = project.Documents[document];
                    FileStamp refreshed = check.RefreshedDocuments[document];
                    if (refreshed is { Exists: true, Shape: not null, Sha256: not null }
                        && string.Equals(refreshed.Sha256, recorded.Sha256, StringComparison.Ordinal))
                        unchanged[refreshed.Path] = refreshed;
                }

            validated.Add(new ValidatedProject(entry, unchanged));
        }

        return new ValidatedStamps(refreshedStructural, validated, dirtyProjects);
    }

    /// <summary>
    ///     The project entries a hit promotes: the manifest's own, rebuilt in manifest order — which is what
    ///     <see cref="DocumentStampsEqual" />'s index-wise compare relies on — carrying the refreshed stamps
    ///     and the <em>true</em> keys.
    /// </summary>
    /// <remarks>
    ///     The keys must be the true ones because that manifest is what the next validation stats against:
    ///     write the decision key and the next run compares a fresh hash to a stale key and calls the project
    ///     dirty forever. The surface keys need no such care: a remap is a trivia-only edit, which no surface
    ///     sees, so the <paramref name="surfaceMemo" /> the dirty pass filled answers every dependency here.
    /// </remarks>
    private static IReadOnlyList<ProjectCacheEntry> RefreshedProjects(
        IReadOnlyList<ProjectCacheEntry> projects,
        IReadOnlyList<ProjectCheck> checkedByIndex,
        IReadOnlyDictionary<string, string> surfaceKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>> referencesByName,
        Dictionary<string, string> surfaceMemo)
    {
        var refreshed = new List<ProjectCacheEntry>(projects.Count);
        for (var index = 0; index < projects.Count; index++)
        {
            ProjectCacheEntry project = projects[index];
            ProjectCheck check = checkedByIndex[index];
            string trueMerkle = ComputeMerkleKey(
                check.TrueContentKey, project.ProjectName, surfaceKeys, referencesByName, surfaceMemo);
            refreshed.Add(project with
            {
                Documents = check.RefreshedDocuments,
                ContentKey = check.TrueContentKey,
                MerkleKey = trueMerkle,
                SurfaceKey = check.SurfaceKey
            });
        }

        return refreshed;
    }

    /// <summary>
    ///     <paramref name="fragments" /> in place and in order, with every fragment of a clean project that
    ///     recorded line maps moved through them. A fragment the maps cannot place puts <em>its own</em>
    ///     project into <paramref name="dirtyProjects" /> — and no dependent, because the equivalence a
    ///     dependent relies on is about this project's compiled surface, which a trivia-only edit leaves
    ///     exactly as it was.
    /// </summary>
    private static IReadOnlyList<CodebaseFragment> Replay(
        IReadOnlyList<CodebaseFragment> fragments,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, LineMap>> mapsByProject,
        HashSet<string> dirtyProjects)
    {
        if (mapsByProject.Count == 0) return fragments;

        var replayed = new List<CodebaseFragment>(fragments.Count);
        foreach (CodebaseFragment fragment in fragments)
        {
            if (dirtyProjects.Contains(fragment.ProjectName)
                || !mapsByProject.TryGetValue(fragment.ProjectName, out IReadOnlyDictionary<string, LineMap>? maps))
            {
                replayed.Add(fragment);
                continue;
            }

            CodebaseFragment? moved = FragmentSiteRemapper.TryRemap(fragment, maps);
            if (moved is null) dirtyProjects.Add(fragment.ProjectName);

            replayed.Add(moved ?? fragment);
        }

        return replayed;
    }

    // The paths of every map that actually stood: a project the Merkle pass or the replay dirtied is
    // re-extracted, so its documents did not move sites instead of dirtying it.
    private static IReadOnlySet<string> RemappedDocumentsOf(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, LineMap>> mapsByProject, HashSet<string> dirtyProjects)
    {
        if (mapsByProject.Count == 0) return NoDocuments;

        var moved = new HashSet<string>(PathComparison.Comparer);
        foreach ((string name, IReadOnlyDictionary<string, LineMap> maps) in mapsByProject)
        {
            if (dirtyProjects.Contains(name)) continue;

            foreach (string path in maps.Keys) moved.Add(path);
        }

        return moved;
    }

    /// <summary>
    ///     One project's verdict from the per-document sweep: the two content keys, the stamps to carry
    ///     forward, and the line maps a trivia-only edit produced.
    /// </summary>
    /// <remarks>
    ///     The two keys are the same string unless <see cref="Maps" /> is non-null.
    ///     <see cref="DecisionContentKey" /> is keyed on the hashes the stored fragments were extracted from,
    ///     so it — and every Merkle key above it — is what decides dirtiness; <see cref="TrueContentKey" />
    ///     is keyed on the bytes now on disk and belongs to the manifest a hit promotes, whose whole job is
    ///     to describe disk. <see cref="SurfaceKey" /> is one key, not two, because the edit that splits the
    ///     content keys is the one edit no surface can see.
    /// </remarks>
    private sealed record ProjectCheck(
        string ProjectName,
        IReadOnlyList<string> ProjectReferences,
        string DecisionContentKey,
        string TrueContentKey,
        string SurfaceKey,
        IReadOnlyList<FileStamp> RefreshedDocuments,
        IReadOnlyDictionary<string, LineMap>? Maps);

    /// <summary>
    ///     One document's verdict from the sweep: the hash each of its project's two content keys takes, the
    ///     stamp to carry forward, and the line map a trivia-only edit produced. Both hashes leave together,
    ///     so no outcome can feed one key and forget the other — the two lists are compared index-wise.
    /// </summary>
    private sealed record DocumentCheck(string? DecisionSha, string? TrueSha, FileStamp Refreshed, LineMap? Map);

    private ProjectCheck CheckProject(
        ProjectCacheEntry project, IReadOnlyDictionary<string, string?> structuralShaByPath, CancellationToken ct)
    {
        var decisionShas = new List<(string Path, string? Sha)>(project.Documents.Count);
        var trueShas = new List<(string Path, string? Sha)>(project.Documents.Count);
        var refreshedDocuments = new List<FileStamp>(project.Documents.Count);
        Dictionary<string, LineMap>? maps = null;

        foreach (FileStamp document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();

            DocumentCheck check = CheckDocument(document);
            decisionShas.Add((document.Path, check.DecisionSha));
            trueShas.Add((document.Path, check.TrueSha));
            refreshedDocuments.Add(check.Refreshed);
            if (check.Map is { } map)
            {
                maps ??= new Dictionary<string, LineMap>(PathComparison.Comparer);
                maps[document.Path] = map;
            }
        }

        (string? csprojSha, string? assetsSha, IReadOnlyList<string> adds) = KeyInputsOf(
            project.CsprojPath, project.ProjectDirectory, project.Documents, structuralShaByPath);
        string decisionKey = ComputeContentKey(project.ProjectName, decisionShas, csprojSha, assetsSha, adds);
        string trueKey = maps is null
            ? decisionKey
            : ComputeContentKey(project.ProjectName, trueShas, csprojSha, assetsSha, adds);
        string surfaceKey = ComputeContentKey(project.ProjectName, SurfacesOf(refreshedDocuments), csprojSha, assetsSha, adds);
        return new ProjectCheck(
            project.ProjectName, project.ProjectReferences, decisionKey, trueKey, surfaceKey, refreshedDocuments, maps);
    }

    // What each document contributes to its project's surface key. The stamp is the one CheckDocument carries
    // forward, whose shape describes exactly the bytes its hash does in every branch, so its surface is
    // right whatever the sweep found. A stamp with no shape — unreadable at capture, or a structural-style
    // stamp — contributes its hash, which can only make a dependent re-walk more often, never less.
    private static List<(string Path, string? Surface)> SurfacesOf(IReadOnlyList<FileStamp> documents)
    {
        return documents
            .Select(d => (d.Path, d.Exists ? d.Shape?.SurfaceHash ?? d.Sha256 : null))
            .ToList();
    }

    private DocumentCheck CheckDocument(FileStamp document)
    {
        FileFreshness current = FileFreshness.Capture(document.Path);
        if (!current.Exists)
        {
            // A deleted document is a content change for its project (the MISSING sentinel drives the key).
            FileStamp refreshed = document with
            {
                Exists = false, LastWriteTimeUtcTicks = 0, Length = 0, Sha256 = null, Promoted = false, Shape = null
            };
            return new DocumentCheck(null, null, refreshed, null);
        }

        if (document.IsTrustedBy(current))
            // provably unchanged: trust the recorded hash
            return new DocumentCheck(document.Sha256, document.Sha256, document, null);

        if (document.Shape is not { } recordedShape)
        {
            // No recorded shape — a structural-style stamp, or a document that was unreadable at capture.
            // Nothing to compare, so the hash alone decides.
            string? sha = HashDuringValidation(document.Path);
            FileStamp refreshed = FileStamping.RefreshStamp(document.Path, current, sha);
            return new DocumentCheck(sha, sha, refreshed, null);
        }

        (string Sha256, SourceShape Shape)? read = ReadDuringValidation(document.Path);
        if (read is not { } fresh)
        {
            // Unreadable now: the MISSING sentinel, the same answer a failed hash gives.
            FileStamp refreshed = FileStamping.RefreshStamp(document.Path, current, null);
            return new DocumentCheck(null, null, refreshed, null);
        }

        if (string.Equals(fresh.Sha256, document.Sha256, StringComparison.Ordinal))
        {
            // A bare touch: the recorded shape still describes these bytes, so it rides forward and the
            // next sweep is pure-stat.
            FileStamp refreshed = FileStamping.RefreshStamp(document.Path, current, document.Sha256, recordedShape);
            return new DocumentCheck(document.Sha256, document.Sha256, refreshed, null);
        }

        LineMap? map = SourceShape.TryMapLines(recordedShape, fresh.Shape);
        if (map is null)
        {
            // A real content change: both keys take the new hash, and the stamp the new shape.
            FileStamp refreshed = FileStamping.RefreshStamp(document.Path, current, fresh.Sha256, fresh.Shape);
            return new DocumentCheck(fresh.Sha256, fresh.Sha256, refreshed, null);
        }

        // Trivia only: the decision key sees the hash the fragments were extracted from, the true key the
        // hash on disk, and the map moves this document's sites.
        FileStamp remapped = FileStamping.RefreshStamp(document.Path, current, fresh.Sha256, fresh.Shape);
        return new DocumentCheck(document.Sha256, fresh.Sha256, remapped, map);
    }

    // ContentKey(P) = hash over P's document hashes (path + sha, deleted ⇒ MISSING), its structural inputs
    // (csproj + assets hashes), and any cone-add paths. Changes iff P's own content changes. Sorted by path so
    // capture and validation agree regardless of input order. Fed to the digest a line at a time rather than
    // as one assembled string: a large project's key would otherwise materialize a megabyte-scale string and
    // its UTF-8 copy, both LOH-bound, per project per run. The byte sequence and its order are significant:
    // change either and every persisted cache file stops matching.
    private static string ComputeContentKey(
        string projectName,
        IReadOnlyList<(string Path, string? Sha)> documents,
        string? csprojSha,
        string? assetsSha,
        IReadOnlyList<string> adds)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, $"project\0{projectName}\n");

        // Folded once per document, then sorted on the folded form — one fold per document rather than one
        // per comparison.
        IOrderedEnumerable<(string Path, string? Sha)> keyed = documents
            .Select(d => d with { Path = PathComparison.Fold(d.Path) })
            .OrderBy(d => d.Path, StringComparer.Ordinal);
        foreach ((string path, string? sha) in keyed)
            Append(hash, $"doc\0{path}\0{sha ?? "MISSING"}\n");

        Append(hash, $"csproj\0{csprojSha ?? "NONE"}\n");
        Append(hash, $"assets\0{assetsSha ?? "NONE"}\n");
        foreach (string add in adds) // already ordinal-sorted
            Append(hash, $"add\0{PathComparison.Fold(add)}\n");

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string line)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(line));
    }

    // MerkleKey(P) = hash(ContentKey(P), SurfaceMerkle(dep) for each dep in name order), where
    // SurfaceMerkle(P) = hash(SurfaceKey(P), SurfaceMerkle(dep) for each dep in name order).
    private static string ComputeMerkleKey(
        string contentKey,
        string name,
        IReadOnlyDictionary<string, string> surfaceKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>> referencesByName,
        Dictionary<string, string> surfaceMemo)
    {
        var builder = new StringBuilder();
        builder.Append(contentKey);
        AppendDependencySurfaces(
            builder, name, surfaceKeys, referencesByName, surfaceMemo, new HashSet<string>(StringComparer.Ordinal));

        return FileStamping.HashText(builder.ToString());
    }

    // Memoized, since every dependent asks for the same dependencies' keys; a visiting set guards against a
    // (should-not-happen) reference cycle.
    private static string ComputeSurfaceMerkleKey(
        string name,
        IReadOnlyDictionary<string, string> surfaceKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>> referencesByName,
        Dictionary<string, string> memo,
        HashSet<string> visiting)
    {
        if (memo.TryGetValue(name, out string? cached)) return cached;

        string self = surfaceKeys.GetValueOrDefault(name, "UNKNOWN");
        if (!visiting.Add(name)) return self; // cycle backstop

        var builder = new StringBuilder();
        builder.Append(self);
        AppendDependencySurfaces(builder, name, surfaceKeys, referencesByName, memo, visiting);

        visiting.Remove(name);
        string key = FileStamping.HashText(builder.ToString());
        memo[name] = key;
        return key;
    }

    // Each dependency of name that has a surface key, in name order, as '\0' and its surface Merkle key. Every
    // ComputeSurfaceMerkleKey call removes from visiting what it adds, so one set serves a whole sibling list.
    private static void AppendDependencySurfaces(
        StringBuilder builder,
        string name,
        IReadOnlyDictionary<string, string> surfaceKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>> referencesByName,
        Dictionary<string, string> memo,
        HashSet<string> visiting)
    {
        if (!referencesByName.TryGetValue(name, out IReadOnlyList<string>? dependencies)) return;

        foreach (string dependency in dependencies.OrderBy(d => d, StringComparer.Ordinal))
            if (surfaceKeys.ContainsKey(dependency))
                builder.Append('\0')
                    .Append(ComputeSurfaceMerkleKey(dependency, surfaceKeys, referencesByName, memo, visiting));
    }

    // The stamp comparison covers the remap case too: a document whose sites moved is a document whose hash
    // and shape changed, so its stamp did.
    private void PromoteIfChanged(
        CacheManifest manifest,
        IReadOnlyList<FileStamp> refreshedStructural,
        IReadOnlyList<ProjectCacheEntry> refreshedProjects,
        IReadOnlyList<CodebaseFragment> replayedFragments)
    {
        bool changed = !FileStamping.StampsEqual(manifest.StructuralStamps, refreshedStructural)
                       || !DocumentStampsEqual(manifest.Projects, refreshedProjects);
        if (!changed) return; // already reflects disk — steady state writes nothing

        CacheManifest promoted = manifest with
        {
            StructuralStamps = refreshedStructural, Projects = refreshedProjects, Fragments = replayedFragments
        };
        TryWriteAtomic(promoted); // best-effort; a later change is still caught by the next validation's stat delta
    }

    // No File.Exists probe: an absent cache and a torn one both read back as null, and both mean a Miss here.
    private bool TryWriteAtomic(CacheManifest manifest)
    {
        return ManifestJson.TryWriteAtomic(cacheFilePath, manifest, ManifestJson.Context.CacheManifest);
    }

    private CacheManifest? TryRead()
    {
        return ManifestJson.TryRead(cacheFilePath, ManifestJson.Context.CacheManifest);
    }

    // A document stamp parses the document for its shape, so one project's documents are stamped in
    // parallel too: stamped one after another, the largest project would be the whole capture's critical
    // path. Position-indexed, so the stamps keep the document order the manifest is compared in.
    private IReadOnlyList<FileStamp> StampDocuments(IReadOnlyList<string> paths, CancellationToken ct)
    {
        var stamps = new FileStamp[paths.Count];
        Parallel.For(0, paths.Count, new ParallelOptions { CancellationToken = ct },
            index => stamps[index] = StampDocumentForCapture(paths[index]));
        return stamps;
    }

    private FileStamp StampDocumentForCapture(string path)
    {
        Interlocked.Increment(ref captureStampCount);
        return FileStamping.StampDocument(path);
    }

    private static bool StatChangedSinceCapture(FileStamp stamp)
    {
        return !stamp.ToFreshness().MatchesStat(FileFreshness.Capture(stamp.Path));
    }

    private string? HashDuringValidation(string path)
    {
        Interlocked.Increment(ref contentHashCount);
        return FileStamping.TryHashFile(path);
    }

    // One content read, counted once: the hash and the shape come off the same bytes, so a shape comparison
    // adds no read to a validation.
    private (string Sha256, SourceShape Shape)? ReadDuringValidation(string path)
    {
        Interlocked.Increment(ref contentHashCount);
        return FileStamping.TryReadDocument(path);
    }

    private static Dictionary<string, string?> BuildStructuralShaLookup(IReadOnlyList<FileStamp> stamps)
    {
        var map = new Dictionary<string, string?>(PathComparison.Comparer);
        foreach (FileStamp stamp in stamps) map[stamp.Path] = stamp.Sha256;
        return map;
    }

    private static bool DocumentStampsEqual(IReadOnlyList<ProjectCacheEntry> left, IReadOnlyList<ProjectCacheEntry> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
            if (!FileStamping.StampsEqual(left[i].Documents, right[i].Documents))
                return false;
        return true;
    }
}
