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
///     The result of <see cref="ExtractionCacheStore.ReadAndValidate" />: what to reuse and what to redo.
///     Carries everything a caller needs to finish a run without re-reading the cache.
/// </summary>
/// <remarks>
///     The recorded spec resolutions and the recorded load verdict are replayed on a hit, so cached and cold
///     output — and the fail-closed decision — are identical on a solution that does not load completely or
///     did not restore.
/// </remarks>
/// <param name="Outcome">What the validation decided — hit, partial, or miss.</param>
/// <param name="ReusableFragments">The clean projects' fragments, reusable as stored.</param>
/// <param name="DirtyProjects">The project names to re-extract on a <see cref="CacheOutcome.Partial" /> read.</param>
/// <param name="SpecResolutions">The recorded spec resolutions a hit replays without a workspace.</param>
/// <param name="LoadDiagnostics">
///     The recorded load verdict, carried whole rather than as the separate lists the manifest stores it in:
///     every surface downstream reads this one value, so a hit hands them exactly what a cold load hands
///     them and no caller can pair the lists up differently. The merge's two facts, its notes and its
///     multi-targeted projects, are empty: the cache stores fragments, and every path regenerates both
///     from them at merge time.
/// </param>
/// <param name="Validated">
///     On a <see cref="CacheOutcome.Partial" /> read, the stamps and keys the validation already took, for
///     <see cref="ExtractionCacheStore.TryCaptureOverValidation" /> to carry rather than re-read; null on a hit and a
///     miss.
/// </param>
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
///     <para>
///         A partial is never a structural change: any change to the solution, a project file, an assets file or
///         a probe file is a miss. So the structural stamps, the project set and the reference graph a partial
///         validated are the ones the capture would take again, and a clean project's documents, true content
///         key and surface key are exactly what a full re-stamp would recompute.
///     </para>
///     <para>
///         Every carried stamp is older than the extraction it vouches for. An edit after validation leaves a
///         stamp that no longer matches disk, so the next validation's stat sees it and re-reads the file, under
///         the same racy-window rule as any other stamp. That is why <see cref="ExtractionCacheStore.Write" />
///         need not re-stat what was carried.
///     </para>
/// </remarks>
/// <param name="StructuralStamps">The validated structural stamps, in manifest order.</param>
/// <param name="Projects">Every manifest project as validation left it, in manifest order.</param>
/// <param name="DirtyProjects">The projects the run re-extracts; every other one is carried whole.</param>
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

/// <summary>One manifest project as a <see cref="CacheOutcome.Partial" /> validation left it.</summary>
/// <param name="Entry">
///     The manifest entry with the refreshed document stamps, the true content key and the surface key. Its
///     Merkle key is the recorded one, which a capture recomputes.
/// </param>
/// <param name="UnchangedDocuments">
///     The refreshed stamps of the documents whose bytes validation found unchanged, by path. A capture reuses
///     these for a dirty project and stamps only the rest.
/// </param>
internal sealed record ValidatedProject(
    ProjectCacheEntry Entry,
    IReadOnlyDictionary<string, FileStamp> UnchangedDocuments);

/// <summary>
///     One project's identity as the caller knows it before any file is statted — the store fills in every
///     stamp and key itself, so the hashing lives in exactly one place and read/write agree by construction.
/// </summary>
/// <param name="ProjectName">The project (assembly) name.</param>
/// <param name="CsprojPath">The project file path.</param>
/// <param name="ProjectDirectory">The project directory (its cone is scanned for added <c>*.cs</c>).</param>
/// <param name="ProjectReferences">
///     The names of the projects this one references (Merkle dependency edges): the compiler's closure, which
///     invalidation has to follow, and not the declared list the fragments carry.
/// </param>
/// <param name="DocumentPaths">The project's source-document paths.</param>
/// <param name="EvaluatedOutputPath">
///     The project's evaluated output path, or null when the workspace carried none. Paired with
///     <paramref name="IntermediateAssemblyPath" /> it is what locates the restore assets file under a
///     non-default output layout (see <see cref="IntermediateOutputTree" />).
/// </param>
/// <param name="IntermediateAssemblyPath">The project's intermediate assembly path, or null when unknown.</param>
internal sealed record ProjectInputs(
    string ProjectName,
    string CsprojPath,
    string ProjectDirectory,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> DocumentPaths,
    string? EvaluatedOutputPath = null,
    string? IntermediateAssemblyPath = null);

/// <summary>
///     A pre-extraction fingerprint of the workspace's files: the structural stamps and per-project entries
///     (with content and Merkle keys) as they were before extraction started. Handed back to
///     <see cref="ExtractionCacheStore.Write" />, which re-stats every input and commits only if nothing has
///     changed since — the guard that stops a mid-run edit from poisoning the cache.
/// </summary>
/// <param name="StructuralStamps">The structural stamps, as the manifest will record them.</param>
/// <param name="Projects">The per-project entries, as the manifest will record them.</param>
/// <param name="FreshStamps">
///     The stamps this capture took from disk when it carried the rest from a partial validation, and so the
///     only ones <see cref="ExtractionCacheStore.Write" /> re-stats; null when the capture stamped everything.
/// </param>
internal sealed record CacheFingerprint(
    IReadOnlyList<FileStamp> StructuralStamps,
    IReadOnlyList<ProjectCacheEntry> Projects,
    IReadOnlyList<FileStamp>? FreshStamps = null);

/// <summary>
///     What a workspace-loaded run produced and wants persisted: the fragments, the spec resolutions to
///     replay, and the load verdict whole — the same value the run itself rendered from, so a hit replays it
///     rather than a re-paired copy of it. The manifest persists its load lists; the merge notes and
///     multi-targeted projects it carries are regenerated from the fragments on every path and are never
///     stored.
/// </summary>
internal sealed record ExtractionResult(
    IReadOnlyList<CodebaseFragment> Fragments,
    IReadOnlyList<SpecResolutionRecord> SpecResolutions,
    WorkspaceDiagnostics LoadDiagnostics);

/// <summary>
///     The read/validate/write boundary over one solution's persisted extraction cache — a single atomic
///     <c>cache.json</c> holding the manifest and every fragment. Validation runs with zero
///     MSBuild: it stats (and selectively re-hashes) the recorded inputs, scans each project cone for added
///     source, and recomputes the content/Merkle keys to produce a dirty set.
/// </summary>
/// <remarks>
///     <para>
///         A write goes through <see cref="AtomicFile" />, so a reader never sees a half-written file. Any
///         torn, garbled, or hand-edited content degrades to a parse-error <see cref="CacheOutcome.Miss" /> —
///         the cache is disposable local derived data, so unlike a baseline it has no tamper story: a bad file
///         is ignored and rebuilt, never a loud error and never a wrong answer.
///     </para>
///     <para>
///         Racy-window and structural semantics mirror the warm <see cref="WorkspaceSession" />'s
///         per-call reconcile sweep (the reference for probe chains, absence recording, and the cone scan),
///         reusing <see cref="FileFreshness" /> for the in-memory stat comparison. The one refinement a
///         persisted cache adds over the warm sweep is content-verification of structural files: a bare
///         mtime touch on a csproj whose bytes are unchanged is not a miss, because a false miss here costs a
///         full cold rebuild.
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
    // a clean Miss — the cache is disposable derived data, so a schema it cannot read is rebuilt, never a loud
    // error. Bump this whenever a fragment gains a fact, OR changes how one is computed: a widened fact keeps
    // its name and its type while taking a different value for identical inputs, so a hit would replay the old
    // answer forever with nothing to distinguish it. The same holds when a manifest record gains a slot, even
    // one that deserializes to a harmless default, so a reader never reasons about which slots a file
    // predates. The suite cannot catch a missed bump — every run gets a fresh cache directory — so the
    // discipline is the only guard.
    private const int CurrentSchemaVersion = 37;

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
    ///     The number of file-content reads (SHA-256 computations) performed by validation. Zero in the
    ///     steady state, where every input is trusted on stat alone. Internal test observable — the
    ///     persisted-cache analog of <see cref="WorkspaceSession.SweepContentReads" />; never consulted in
    ///     production.
    /// </summary>
    /// <remarks>
    ///     Counted through <see cref="Interlocked" /> because the per-project sweep is parallel, and the
    ///     tests that read this assert exact deltas — a lost increment would not be a slightly-off number, it
    ///     would be a flaky pin on the promise that a steady-state validation opens nothing.
    /// </remarks>
    internal long ContentHashCount => Volatile.Read(ref contentHashCount);

    /// <summary>
    ///     The absolute paths of the documents the last <see cref="ReadAndValidate" /> moved sites for
    ///     instead of dirtying their project — empty in the steady state and on every miss. Internal test
    ///     observable; never printed.
    /// </summary>
    internal IReadOnlySet<string> LastRemappedDocuments { get; private set; } = NoDocuments;

    /// <summary>
    ///     The number of documents a capture read from disk to stamp. A capture handed a partial validation's
    ///     stamps reads only the documents that changed or are new. Internal test observable, counted through
    ///     <see cref="Interlocked" /> for the same reason <see cref="ContentHashCount" /> is; never consulted in
    ///     production.
    /// </summary>
    internal long CaptureStampCount => Volatile.Read(ref captureStampCount);

    /// <summary>
    ///     Stats and hashes every input <em>now</em>, returning the pre-extraction fingerprint to hand to
    ///     <see cref="Write" /> after extraction. Does not touch <see cref="ContentHashCount" /> (that counts
    ///     validation reads only) and writes nothing.
    /// </summary>
    public CacheFingerprint CaptureFingerprint(IReadOnlyList<ProjectInputs> projects, CancellationToken ct = default)
    {
        return CaptureFingerprint(projects, null, ct);
    }

    /// <summary>
    ///     The fingerprint to hand to <see cref="Write" />, carrying what <paramref name="validated" /> already
    ///     stamped. A clean project is taken whole from the validation; a dirty project keeps the stamps of the
    ///     documents validation found unchanged and stamps only the ones that changed or are new, from the
    ///     loaded <paramref name="projects" />, which decide its document list. Without a validation, or when
    ///     <paramref name="projects" /> holds a project the validation does not know or lacks a dirty one, every
    ///     input is stamped now.
    /// </summary>
    /// <param name="projects">
    ///     The loaded projects. Under a load of only the dirty projects' closure this is a subset of the
    ///     manifest's, which is enough: every clean project comes from <paramref name="validated" />.
    /// </param>
    /// <param name="validated">The partial validation's stamps, or null to stamp everything.</param>
    /// <param name="ct">Cancellation token.</param>
    public CacheFingerprint CaptureFingerprint(
        IReadOnlyList<ProjectInputs> projects, ValidatedStamps? validated, CancellationToken ct = default)
    {
        return TryCaptureOverValidation(projects, validated, ct) ?? CaptureEverything(projects, ct);
    }

    /// <summary>
    ///     The fingerprint that carries <paramref name="validated" />, or null when it cannot: no validation, or
    ///     <paramref name="projects" /> holds a project the validation does not know or lacks a dirty one. A
    ///     caller whose <paramref name="projects" /> are only a closure must not fall back to stamping them alone,
    ///     because a manifest without the projects it did not load would leave their fragments unvalidated.
    /// </summary>
    /// <param name="projects">The loaded projects: the whole solution's, or the dirty projects' closure.</param>
    /// <param name="validated">The partial validation's stamps.</param>
    /// <param name="ct">Cancellation token.</param>
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

        // Re-keyed by name in input order, so a repeated name resolves to its last occurrence, as in a full capture.
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

        var contentKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var surfaceKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var referencesByName = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (ProjectCacheEntry entry in entryByIndex)
        {
            contentKeys[entry.ProjectName] = entry.ContentKey;
            surfaceKeys[entry.ProjectName] = entry.SurfaceKey;
            referencesByName[entry.ProjectName] = entry.ProjectReferences;
        }

        // The same Merkle pass a full capture runs, over the same kinds of key, so capture and validation still
        // agree by construction.
        var surfaceMemo = new Dictionary<string, string>(StringComparer.Ordinal);
        List<ProjectCacheEntry> entries = entryByIndex
            .Select(entry => entry with
            {
                MerkleKey = ComputeMerkleKey(entry.ProjectName, contentKeys, surfaceKeys, referencesByName, surfaceMemo)
            })
            .ToList();

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
    // Cone-adds are computed exactly as validation does, over the same known-document set (the stamps' full
    // paths). Hardcoding an empty adds list here would be wrong: a *.cs on disk under the project but excluded
    // from compilation is a validation-time add, so the capture would never validate and the project would stay
    // dirty forever. With capture and validation running the one routine, they agree.
    private static (string ContentKey, string SurfaceKey) ProjectKeysOf(
        ProjectInputs project, IReadOnlyList<FileStamp> documents, IReadOnlyDictionary<string, string?> structuralShaByPath)
    {
        List<(string Path, string? Sha256)> documentShas = documents.Select(d => (d.Path, d.Sha256)).ToList();
        string? csprojSha = structuralShaByPath.GetValueOrDefault(Path.GetFullPath(project.CsprojPath));
        string? assetsSha = structuralShaByPath.GetValueOrDefault(
            IntermediateOutputTree.DefaultAssetsPathOf(project.ProjectDirectory));

        var knownDocuments = new HashSet<string>(documents.Select(d => d.Path), PathComparison.Comparer);
        IReadOnlyList<string> adds = ProjectCone.Adds(Path.GetFullPath(project.ProjectDirectory), knownDocuments);
        return (ComputeContentKey(project.ProjectName, documentShas, csprojSha, assetsSha, adds),
            ComputeContentKey(project.ProjectName, SurfacesOf(documents), csprojSha, assetsSha, adds));
    }

    private CacheFingerprint CaptureEverything(IReadOnlyList<ProjectInputs> projects, CancellationToken ct)
    {
        List<FileStamp> structuralStamps = StructuralPathsOf(projects)
            .Select(FileStamping.StampOf)
            .ToList();

        Dictionary<string, string?> structuralShaByPath = BuildStructuralShaLookup(structuralStamps);

        // Each project's fingerprint reads only its own files, so the whole hash-and-parse pass over the
        // solution's sources runs in parallel — the one place a cold run spends real wall-clock — and lands
        // in position-indexed slots. The Merkle pass below needs every content key before it can start, so
        // it stays sequential.
        var contentKeyByIndex = new string[projects.Count];
        var surfaceKeyByIndex = new string[projects.Count];
        var documentsByIndex = new IReadOnlyList<FileStamp>[projects.Count];

        Parallel.For(0, projects.Count, new ParallelOptions { CancellationToken = ct }, index =>
        {
            ProjectInputs project = projects[index];

            IReadOnlyList<FileStamp> documents = StampDocuments(project.DocumentPaths, ct);
            (contentKeyByIndex[index], surfaceKeyByIndex[index]) = ProjectKeysOf(project, documents, structuralShaByPath);
            documentsByIndex[index] = documents;
        });

        // Re-keyed by name in input order, so a repeated name resolves to its last occurrence.
        var contentKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var surfaceKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var referencesByName = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var documentsByName = new Dictionary<string, IReadOnlyList<FileStamp>>(StringComparer.Ordinal);
        for (var index = 0; index < projects.Count; index++)
        {
            ProjectInputs project = projects[index];
            contentKeys[project.ProjectName] = contentKeyByIndex[index];
            surfaceKeys[project.ProjectName] = surfaceKeyByIndex[index];
            referencesByName[project.ProjectName] = project.ProjectReferences;
            documentsByName[project.ProjectName] = documentsByIndex[index];
        }

        var surfaceMemo = new Dictionary<string, string>(StringComparer.Ordinal);
        var entries = new List<ProjectCacheEntry>(projects.Count);
        foreach (ProjectInputs project in projects)
        {
            string merkleKey = ComputeMerkleKey(project.ProjectName, contentKeys, surfaceKeys, referencesByName, surfaceMemo);
            entries.Add(new ProjectCacheEntry(
                project.ProjectName,
                Path.GetFullPath(project.CsprojPath),
                Path.GetFullPath(project.ProjectDirectory),
                project.ProjectReferences,
                documentsByName[project.ProjectName],
                contentKeys[project.ProjectName],
                merkleKey,
                surfaceKeys[project.ProjectName]));
        }

        return new CacheFingerprint(structuralStamps, entries);
    }

    /// <summary>
    ///     Commits <paramref name="extraction" /> to the cache, keyed to <paramref name="fingerprint" />.
    ///     Re-stats every input the capture took from disk first; if any changed since
    ///     <see cref="CaptureFingerprint(IReadOnlyList{ProjectInputs}, CancellationToken)" /> (a mid-run edit),
    ///     skips the write and returns <c>false</c>. A best-effort I/O failure also returns <c>false</c>.
    ///     Otherwise writes atomically and returns <c>true</c>.
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
        var refreshedStructural = new List<FileStamp>(manifest.StructuralStamps.Count);
        foreach (FileStamp stamp in manifest.StructuralStamps)
        {
            ct.ThrowIfCancellationRequested();
            (bool missed, FileStamp refreshed) = FileStamping.CheckStructural(
                stamp, () => Interlocked.Increment(ref contentHashCount));
            if (missed) return CacheReadResult.Miss();
            refreshedStructural.Add(refreshed);
        }

        Dictionary<string, string?> structuralShaByPath = BuildStructuralShaLookup(refreshedStructural);

        // Per-document sweep + cone scan ⇒ each project's recomputed content keys. Independent per project —
        // each reads only its own documents and its own cone — so it runs in parallel into position-indexed
        // slots; the Merkle pass below needs them all and stays sequential.
        var checkedByIndex = new ProjectCheck[manifest.Projects.Count];

        Parallel.For(0, manifest.Projects.Count, new ParallelOptions { CancellationToken = ct },
            index => checkedByIndex[index] = CheckProject(manifest.Projects[index], structuralShaByPath, ct));

        var decisionContentKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var trueContentKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var surfaceKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var referencesByName = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var mapsByProject = new Dictionary<string, IReadOnlyDictionary<string, LineMap>>(StringComparer.Ordinal);
        foreach (ProjectCheck check in checkedByIndex)
        {
            decisionContentKeys[check.ProjectName] = check.DecisionContentKey;
            trueContentKeys[check.ProjectName] = check.TrueContentKey;
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
        foreach (ProjectCacheEntry project in manifest.Projects)
        {
            string recomputedMerkle = ComputeMerkleKey(
                project.ProjectName, decisionContentKeys, surfaceKeys, referencesByName, surfaceMemo);
            if (!string.Equals(recomputedMerkle, project.MerkleKey, StringComparison.Ordinal))
                dirtyProjects.Add(project.ProjectName);
        }

        IReadOnlyList<CodebaseFragment> replayedFragments = Replay(manifest.Fragments, mapsByProject, dirtyProjects);
        LastRemappedDocuments = RemappedDocumentsOf(mapsByProject, dirtyProjects);

        // Re-paired once, here, from the flat lists the manifest persists — the only place the five become a
        // verdict again, so no consumer can assemble them in a different order. The merge's own two facts —
        // the notes and the multi-targeted projects — are empty here on purpose: nothing persists them
        // because a hit re-merges the stored fragments and produces both afresh.
        var loadDiagnostics = new WorkspaceDiagnostics(
            manifest.Diagnostics, [], manifest.FailedProjects, manifest.UncheckedProjects,
            manifest.RestoreFailedProjects, manifest.UnsupportedProjects, []);

        if (dirtyProjects.Count == 0)
        {
            // The surface keys are the same on both sides of a remap, so the memo the pass above filled holds
            // every surface Merkle key the promotion needs.
            IReadOnlyList<ProjectCacheEntry> refreshedProjects = RefreshedProjects(
                manifest.Projects, checkedByIndex, trueContentKeys, surfaceKeys, referencesByName, surfaceMemo);
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
        IReadOnlyDictionary<string, string> trueContentKeys,
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
                project.ProjectName, trueContentKeys, surfaceKeys, referencesByName, surfaceMemo);
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
        var knownDocuments = new HashSet<string>(PathComparison.Comparer);
        Dictionary<string, LineMap>? maps = null;

        foreach (FileStamp document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            knownDocuments.Add(document.Path);

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

        // Capture and validation compute cone-adds through the one routine over the same known-document set,
        // so an always-present excluded stray lands in both adds lists and cancels; only a genuine add moves.
        IReadOnlyList<string> adds = ProjectCone.Adds(project.ProjectDirectory, knownDocuments);
        string? csprojSha = structuralShaByPath.GetValueOrDefault(project.CsprojPath);
        string? assetsSha = structuralShaByPath.GetValueOrDefault(
            IntermediateOutputTree.DefaultAssetsPathOf(project.ProjectDirectory));
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

        if (FileStamping.ToFreshness(document).MatchesStat(current) && document.Promoted)
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
    // SurfaceMerkle(P) = hash(SurfaceKey(P), SurfaceMerkle(dep) for each dep in name order). So P re-walks on
    // any change to its own content and on a change to the declaration surface of anything it references,
    // transitively, and on nothing else: a body-only edit in a dependency moves that dependency's content key
    // and leaves every surface key in the graph where it was.
    private static string ComputeMerkleKey(
        string name,
        IReadOnlyDictionary<string, string> contentKeys,
        IReadOnlyDictionary<string, string> surfaceKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>> referencesByName,
        Dictionary<string, string> surfaceMemo)
    {
        var builder = new StringBuilder();
        builder.Append(contentKeys.GetValueOrDefault(name, "UNKNOWN"));
        if (referencesByName.TryGetValue(name, out IReadOnlyList<string>? dependencies))
            foreach (string dependency in dependencies.OrderBy(d => d, StringComparer.Ordinal))
                if (surfaceKeys.ContainsKey(dependency))
                    builder.Append('\0').Append(ComputeSurfaceMerkleKey(
                        dependency, surfaceKeys, referencesByName, surfaceMemo, new HashSet<string>(StringComparer.Ordinal)));

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
        if (referencesByName.TryGetValue(name, out IReadOnlyList<string>? dependencies))
            foreach (string dependency in dependencies.OrderBy(d => d, StringComparer.Ordinal))
                if (surfaceKeys.ContainsKey(dependency))
                    builder.Append('\0').Append(ComputeSurfaceMerkleKey(dependency, surfaceKeys, referencesByName, memo, visiting));

        visiting.Remove(name);
        string key = FileStamping.HashText(builder.ToString());
        memo[name] = key;
        return key;
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

    // ManifestJson owns both halves and their degradation contract. No File.Exists probe: an absent cache
    // reads back as null through the same catch a torn one does, and both mean the same thing here — a Miss.
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
        FileFreshness current = FileFreshness.Capture(stamp.Path);
        if (stamp.Exists != current.Exists) return true;
        if (!stamp.Exists) return false;
        return !FileStamping.ToFreshness(stamp).MatchesStat(current);
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

    // ProjectCone owns the composition, so the build capture stamps the identical set from the identical
    // routine.
    private IReadOnlyList<string> StructuralPathsOf(IReadOnlyList<ProjectInputs> projects)
    {
        return ProjectCone.SolutionStructuralPaths(
            solutionPath,
            projects.Select(project => (
                Path.GetFullPath(project.CsprojPath),
                Path.GetFullPath(project.ProjectDirectory),
                project.EvaluatedOutputPath,
                project.IntermediateAssemblyPath)));
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
