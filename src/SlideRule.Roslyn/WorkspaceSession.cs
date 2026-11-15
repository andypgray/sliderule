using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Internal;
using SlideRule.Roslyn.Caching;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Sessions;
using SlideRule.Roslyn.Solutions;
using SlideRule.Roslyn.Threading;

namespace SlideRule.Roslyn;

/// <summary>
///     A loaded solution kept alive for a host that reads it many times. Each
///     <see cref="GetCurrentAsync(string, CancellationToken)" /> reconciles the solution against disk and hands back
///     an immutable <see cref="WorkspaceSnapshot" />. The session owns the MSBuild workspace and the out-of-process
///     build host behind it, so dispose it when the host shuts down. For a single read, use
///     <see cref="WorkspaceLoader" /> instead.
/// </summary>
/// <remarks>
///     <para>
///         Nothing is loaded until the first <see cref="GetCurrentAsync(string, CancellationToken)" />, which pays the
///         whole solution load; later calls pay only the reconcile. A structural change (the solution or filter file,
///         the solution a filter references, a project file, a restore assets file, or any of the
///         <see cref="FileStamping.StructuralProbeFileNames" /> at or above a project), a source file added or
///         deleted, or an edit to a source of a project in another language reloads the solution wholesale. An edit
///         to any other file already in the solution is folded into a fresh snapshot, and an unchanged tree hands
///         back the very snapshot instance the previous call returned.
///     </para>
///     <para>
///         It reads and never writes: no file is touched, and no snapshot is applied back to the workspace.
///         Pointing a session at a working tree somebody else is editing is the expected use. Calls serialize, so
///         several callers asking at once share one load rather than starting several.
///     </para>
/// </remarks>
// The first read pays the whole load rather than the constructor doing it eagerly, so a load failure
// reaches the caller with the same text the one-shot WorkspaceLoader produces; per-call error-text
// parity between the warm and the cold path is pinned.
internal sealed class WorkspaceSession : IAsyncDisposable
{
    private readonly Action<string>? diagnosticSink;

    // Fingerprints of the compiled documents, keyed by canonical full path. A *.cs excluded from compilation
    // (<Compile Remove>, a None item) is absent here yet is not a new file, so this set must not stand in for
    // knownConeFiles.
    private readonly Dictionary<string, FileFreshness> documentFingerprints = new(PathComparison.Comparer);

    // Canonical full path ⇒ the document(s) at that path, keyed as documentFingerprints is. Resolving an edited
    // file through this map avoids Roslyn's own path matching, whose spelling need not equal our normalized key.
    // DocumentIds survive WithDocumentText, so the map stays valid across in-place edits within one load.
    private readonly Dictionary<string, List<DocumentId>> documentIds = new(PathComparison.Comparer);

    // The one gate that serializes every sweep, reload, and edit against concurrent callers.
    private readonly SemaphoreSlim gate = new(1, 1);

    // Every source the cone scan finds under the loaded project directories, snapshotted at load. Only a file the
    // scan finds that is absent here is a newly added source file; a compiled document and an excluded stray
    // both live here, so neither trips it.
    private readonly HashSet<string> knownConeFiles = new(PathComparison.Comparer);

    // Each loaded project's directory with its language's source suffix, scanned for newly-added source files.
    private readonly HashSet<(string Directory, string Suffix)> coneRoots = new(ConeRootComparer.Instance);

    // The documents of projects in other languages. The model holds nothing of theirs, but a C# project binds to
    // their compilation, and the load warns when that compilation reaches it with no assembly; folding an edit in
    // would leave the warning as the load found it, so an edit to one reloads.
    private readonly HashSet<string> foreignDocuments = new(PathComparison.Comparer);

    // The live counters behind WorkspaceSnapshot.ProjectEditVersions; each snapshot takes an immutable copy.
    private readonly Dictionary<string, int> projectEditVersions = new(StringComparer.Ordinal);

    // Structural-file fingerprints, keyed by canonical full path. Absent probe-chain entries are recorded
    // with Exists = false so a file that later appears trips the sweep.
    private readonly Dictionary<string, FileFreshness> structuralFingerprints = new(PathComparison.Comparer);

    // The private snapshot chain: the current (possibly edited) forked solution.
    private Solution? current;

    private int disposed;

    // The load generation, bumped on every full (re)load and stamped onto each snapshot. Starts at 0; the first
    // load makes it 1, so a never-loaded generation never aliases one.
    private long generation;

    // The current generation's load verdict, carried whole onto every snapshot the generation produces.
    private WorkspaceDiagnostics loadVerdict = WorkspaceDiagnostics.None;

    // The owning workspace of the current load generation. Null until the first load, and between a
    // reset-to-unloaded and the next successful load.
    private LoadedSolution? loaded;

    // Canonical path of the currently-loaded solution; drives the "path changed ⇒ full load" branch.
    private string? loadedSolutionPath;

    // The loaded solution's path from the moment its snapshot exists until a load starts. IsLoaded reads it
    // outside the gate, so it is written only through Volatile.
    private string? readySolutionPath;

    // The cached immutable snapshot returned to callers.
    private WorkspaceSnapshot? snapshot;

    private IReadOnlyDictionary<ProjectId, string> targetFrameworks = TargetFrameworkMaps.None;

    // Set with every load, and read only through a snapshot, which is minted after one.
    private CanonicalProjectFiles? projectFiles;

    // Set with every load, as projectFiles is.
    private IReadOnlyDictionary<ProjectId, IReadOnlyList<string>>? compilerAssemblies;

    /// <summary>
    ///     Creates a session with nothing loaded. <paramref name="diagnosticSink" /> receives the session's own
    ///     commentary (why a call reloaded, a file the reconcile could not read), never the load's failures, which
    ///     a snapshot's <see cref="WorkspaceSnapshot.LoadDiagnostics" /> carries.
    /// </summary>
    public WorkspaceSession(Action<string>? diagnosticSink = null)
    {
        this.diagnosticSink = diagnosticSink;
    }

    /// <summary>
    ///     The content reads the per-document reconcile sweep has made, a test observable: a file whose stat
    ///     proves it unchanged is never re-read, so this stays at zero in the steady state.
    /// </summary>
    internal long SweepContentReads { get; private set; }

    /// <summary>The full solution loads this session has made, the first included; a test observable.</summary>
    internal long FullReloadCount { get; private set; }

    /// <summary>
    ///     Tears down the workspace and its build host. A second call is a no-op, and a call already in flight is
    ///     waited for only briefly, so a wedged read cannot hold up process shutdown.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;

        // Bounded acquire: on shutdown, disposing the workspace is the safer outcome even if a sweep is
        // still holding the gate, so proceed after the wait either way. 12 s matches the pre-sized MCP
        // disposer budget (ServerShutdown).
        bool acquired = await gate.WaitAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
        try
        {
            loaded?.Dispose();
            loaded = null;
            current = null;
            snapshot = null;
        }
        finally
        {
            if (acquired) gate.Release();
            gate.Dispose();
        }
    }

    /// <summary>
    ///     Returns the solution at <paramref name="solutionPath" />, the absolute path of a <c>.sln</c>,
    ///     <c>.slnx</c> or <c>.slnf</c> filter, reconciled against disk as of this call. A call naming a different
    ///     path than the last loads it in full. A load that throws leaves the session unloaded, so the next call
    ///     starts again from a fresh workspace rather than serving a half-loaded one.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="UserErrorException">
    ///     The <c>.slnf</c> filter at <paramref name="solutionPath" /> could not be read.
    /// </exception>
    public async Task<WorkspaceSnapshot> GetCurrentAsync(string solutionPath, CancellationToken ct = default)
    {
        ForcedReconcile reconciled = await GetCurrentAsync(solutionPath, [], ct).ConfigureAwait(false);
        return reconciled.Snapshot;
    }

    /// <summary>
    ///     Whether the session holds a loaded solution at <paramref name="solutionPath" /> whose last load
    ///     succeeded. It never loads and never waits on a call in flight, so a load or reload under way answers
    ///     false, and a reconcile under way answers as the load before it left things.
    /// </summary>
    public bool IsLoaded(string solutionPath)
    {
        // The disposed flag, not the field, answers for a disposed session: a load in flight when disposal starts
        // publishes the field again when it finishes.
        return Volatile.Read(ref disposed) == 0
               && PathEquals(Volatile.Read(ref readySolutionPath), Path.GetFullPath(solutionPath));
    }

    /// <summary>
    ///     Returns the solution at <paramref name="solutionPath" /> as the other overload does, except that each
    ///     document among <paramref name="forceRead" /> is read by content even where its stat would vouch for
    ///     it, and a structural file among them reloads the solution, since only a fresh evaluation can show what
    ///     a stat-identical rewrite of one changed. The result says how each listed path was read.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="UserErrorException">
    ///     The <c>.slnf</c> filter at <paramref name="solutionPath" /> could not be read.
    /// </exception>
    public async Task<ForcedReconcile> GetCurrentAsync(
        string solutionPath, IReadOnlyCollection<string> forceRead, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        string normalizedPath = Path.GetFullPath(solutionPath);
        HashSet<string> listed = forceRead.Select(Path.GetFullPath)
            .ToHashSet(PathComparison.Comparer);

        using SemaphoreRelease held = await gate.LockAsync(ct).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(disposed != 0, this);

        // Taken before anything can reload, so a path's stat can be held to the whole of a load.
        Dictionary<string, FileFreshness> statsAtStart = listed.ToDictionary(
            path => path,
            FileFreshness.Capture,
            PathComparison.Comparer);
        long reloadsAtStart = FullReloadCount;
        Dictionary<string, ForcedReadOutcome> outcomes = new(PathComparison.Comparer);

        if (loaded is null || !PathEquals(loadedSolutionPath, normalizedPath))
        {
            await LoadFreshAsync(normalizedPath, ct).ConfigureAwait(false);
        }
        else if (listed.FirstOrDefault(structuralFingerprints.ContainsKey) is { } structural)
        {
            diagnosticSink?.Invoke($"Structural file listed for a forced read, reloading: {structural}");
            await LoadFreshAsync(loadedSolutionPath!, ct).ConfigureAwait(false);
        }
        else
        {
            await ReconcileAsync(listed, outcomes, ct).ConfigureAwait(false);
        }

        // Every path is held to the stat taken before anything in this call read it, never to one the load or the
        // sweep took: MSBuild's evaluation reads a structural file before the load stamps it, so no stat taken
        // inside the call vouches for what that evaluation read.
        bool reloaded = FullReloadCount != reloadsAtStart;
        foreach (string path in listed)
        {
            ForcedReadOutcome read = reloaded
                ? ForcedReadOutcome.Reloaded
                : outcomes.GetValueOrDefault(path, ForcedReadOutcome.Untracked);
            bool stable = statsAtStart[path].MatchesStat(FileFreshness.Capture(path));
            outcomes[path] = !stable && read is ForcedReadOutcome.Read or ForcedReadOutcome.Reloaded
                ? ForcedReadOutcome.Unstable
                : read;
        }

        return new ForcedReconcile(snapshot!, outcomes);
    }

    /// <summary>
    ///     Disposes any current generation and opens a fresh workspace. State is reset to unloaded up front
    ///     so a throwing load leaves nothing half-initialized: the next call retries from scratch.
    /// </summary>
    private async Task LoadFreshAsync(string solutionPath, CancellationToken ct)
    {
        Volatile.Write(ref readySolutionPath, null);
        LoadedSolution? previous = loaded;
        loaded = null;
        current = null;
        loadedSolutionPath = null;
        snapshot = null;
        loadVerdict = WorkspaceDiagnostics.None;
        targetFrameworks = TargetFrameworkMaps.None;
        projectFiles = null;
        compilerAssemblies = null;
        documentFingerprints.Clear();
        documentIds.Clear();
        structuralFingerprints.Clear();
        knownConeFiles.Clear();
        coneRoots.Clear();
        foreignDocuments.Clear();
        projectEditVersions.Clear();

        previous?.Dispose();

        List<string> collected = [];
        Lock collectedLock = new();

        // Set by MaterializeAsync, which a load that returns has always run.
        IReadOnlyDictionary<string, FileFreshness>? statsBeforeRead = null;
        LoadedSolution freshlyLoaded = await WorkspaceLoader
            .LoadAsync(solutionPath, CollectDiagnostic, MaterializeAsync, ct)
            .ConfigureAwait(false);

        loaded = freshlyLoaded;
        current = freshlyLoaded.Solution;
        loadedSolutionPath = solutionPath;
        loadVerdict = freshlyLoaded.LoadDiagnosticsWith(collected);
        targetFrameworks = freshlyLoaded.TargetFrameworks;
        projectFiles = freshlyLoaded.ProjectFiles;
        compilerAssemblies = freshlyLoaded.CompilerAssemblies;
        generation++;
        SeedEditVersions(current);
        RecordAllFingerprints(solutionPath, current, compilerAssemblies, statsBeforeRead!);
        snapshot = MintSnapshot();
        FullReloadCount++;
        Volatile.Write(ref readySolutionPath, solutionPath);
        return;

        async Task<Solution> MaterializeAsync(Solution solution, CancellationToken token)
        {
            (Solution materialized, statsBeforeRead) = await MaterializeDocumentTextsAsync(solution, token)
                .ConfigureAwait(false);
            return materialized;
        }

        // MSBuildWorkspace can raise workspace-failure diagnostics from BuildHost callback threads while
        // the open is in flight, so synchronize the collector even though the load itself is gate-serialized.
        void CollectDiagnostic(string message)
        {
            lock (collectedLock)
            {
                collected.Add(message);
            }
        }
    }

    // forced names documents to read whatever their stat says, and outcomes receives how each one was read;
    // neither is consulted once a reload is decided.
    private async Task ReconcileAsync(
        IReadOnlySet<string> forced, Dictionary<string, ForcedReadOutcome> outcomes, CancellationToken ct)
    {
        // The arms run cheapest first and short-circuit: the document sweep is the only one that reads file
        // content, so it must not run once a reload is already decided.
        if (StructuralSweepDetectsChange()
            || ConeScanDetectsNewFile()
            || await ReconcileDocumentsAsync(forced, outcomes, ct).ConfigureAwait(false))
            await LoadFreshAsync(loadedSolutionPath!, ct).ConfigureAwait(false);
    }

    private bool StructuralSweepDetectsChange()
    {
        foreach ((string path, FileFreshness recorded) in structuralFingerprints)
        {
            FileFreshness now = FileFreshness.Capture(path);
            if (recorded.MatchesStat(now)) continue;

            diagnosticSink?.Invoke($"Structural change detected, reloading: {path}");
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether any project directory's <see cref="Caching.ProjectCone">cone</see> holds a source in its
    ///     project's language missing from <see cref="knownConeFiles" />: the add case an mtime sweep cannot see.
    /// </summary>
    private bool ConeScanDetectsNewFile()
    {
        foreach ((string projectDirectory, string suffix) in coneRoots)
        {
            if (ProjectCone.FirstAdd(projectDirectory, suffix, knownConeFiles) is not { } file) continue;

            diagnosticSink?.Invoke($"New source file detected, reloading: {file}");
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Folds the edited documents into the snapshot chain, content-verifying only what a stat cannot vouch
    ///     for, and returns true when the caller must reload wholesale instead: a document has gone missing, or one
    ///     in a project of another language has new content.
    /// </summary>
    private async Task<bool> ReconcileDocumentsAsync(
        IReadOnlySet<string> forced, Dictionary<string, ForcedReadOutcome> outcomes, CancellationToken ct)
    {
        // Snapshot the key set: the loop reassigns documentFingerprints entries in place.
        List<string> paths = [.. documentFingerprints.Keys];

        foreach (string path in paths)
        {
            ct.ThrowIfCancellationRequested();

            // Snapshot the fingerprint BEFORE reading, so the value we record reflects the version we
            // actually load; a write that interleaves with the read then can't be masked.
            FileFreshness beforeRead = FileFreshness.Capture(path);
            if (!beforeRead.Exists) return true;

            bool isForced = forced.Contains(path);
            if (!isForced && documentFingerprints[path].CanTrust(beforeRead)) continue;

            SourceText text;
            try
            {
                text = await ReadTextAsync(path, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leave the entry stale-fingerprinted (retried next call) rather than poison the sweep.
                diagnosticSink?.Invoke($"Skipping unreadable file during reconcile: {path} ({ex.Message})");
                if (isForced) outcomes[path] = ForcedReadOutcome.Unreadable;
                continue;
            }

            SweepContentReads++;
            if (isForced) outcomes[path] = ForcedReadOutcome.Read;

            if (!documentIds.TryGetValue(path, out List<DocumentId>? ids) || ids.Count == 0)
            {
                // Path recorded but no document maps to it (defensive): re-fingerprint and move on.
                documentFingerprints[path] = beforeRead;
                continue;
            }

            SourceText existing = await current!.GetDocument(ids[0])!.GetTextAsync(ct).ConfigureAwait(false);
            if (existing.ContentEquals(text))
            {
                // Same bytes (an mtime-only touch): keep the snapshot, promote the fingerprint.
                documentFingerprints[path] = beforeRead;
                continue;
            }

            if (foreignDocuments.Contains(path))
            {
                diagnosticSink?.Invoke($"Edit to a source in another language detected, reloading: {path}");
                return true;
            }

            Solution updated = current;
            foreach (DocumentId id in ids)
                updated = updated.WithDocumentText(id, text);

            current = updated;
            documentFingerprints[path] = beforeRead;
            BumpEditVersions(ids);
            snapshot = MintSnapshot();
        }

        return false;
    }

    /// <summary>
    ///     Mints a snapshot of the current solution with an immutable copy of the edit-version map, so a consumer
    ///     holding the snapshot keeps a frozen view while later sweeps bump the live map.
    /// </summary>
    private WorkspaceSnapshot MintSnapshot()
    {
        return new WorkspaceSnapshot(current!, loadVerdict)
        {
            Generation = generation,
            ProjectEditVersions = new Dictionary<string, int>(projectEditVersions, StringComparer.Ordinal),
            TargetFrameworks = targetFrameworks,
            ProjectFiles = projectFiles!,
            CompilerAssemblies = compilerAssemblies!
        };
    }

    /// <summary>
    ///     Reseeds the edit-version map for a fresh generation: every C# project starts at 0, fixing the map's
    ///     key set for the generation's life so two same-generation snapshots differ only where bytes changed.
    /// </summary>
    private void SeedEditVersions(Solution solution)
    {
        projectEditVersions.Clear();
        foreach (Project project in solution.Projects)
            if (ProjectLanguages.IsCSharp(project))
                projectEditVersions[project.Name] = 0;
    }

    /// <summary>
    ///     Bumps the counter of every project owning one of the just-rewritten documents. A multi-targeted
    ///     project's several <see cref="Project" />s share one name
    ///     (<see cref="SolutionExtensions.NormalizeProjectNames" />), so they share one counter, keyed as the
    ///     name-keyed fragment store expects; only that the counter changes matters, not by how much.
    /// </summary>
    private void BumpEditVersions(IReadOnlyList<DocumentId> ids)
    {
        foreach (DocumentId id in ids)
            if (current!.GetProject(id.ProjectId)?.Name is { } name)
                projectEditVersions[name] = projectEditVersions.GetValueOrDefault(name) + 1;
    }

    /// <summary>
    ///     Records the load-time fingerprints, each document's being the stat taken before materialization read it.
    /// </summary>
    /// <remarks>
    ///     The structural set follows the rule
    ///     <see
    ///         cref="Caching.ProjectCone.SolutionStructuralPaths(string, IEnumerable{Caching.ProjectInputs}, IEnumerable{Caching.ForeignProjectInputs})" />
    ///     stamps the persisted caches by, over every project with a file, whatever its language. It is taken per
    ///     <see cref="Project" /> rather than
    ///     per project directory, because where the restore assets file lands follows the project's own evaluated
    ///     output paths and not its directory.
    /// </remarks>
    private void RecordAllFingerprints(
        string solutionPath, Solution solution, IReadOnlyDictionary<ProjectId, IReadOnlyList<string>> compilers,
        IReadOnlyDictionary<string, FileFreshness> statsBeforeRead)
    {
        foreach (Project project in solution.Projects)
        foreach (Document document in project.Documents)
        {
            if (document.FilePath is null) continue;

            string full = Path.GetFullPath(document.FilePath);
            documentFingerprints[full] = statsBeforeRead[full];
            if (!ProjectLanguages.IsCSharp(project)) foreignDocuments.Add(full);
            if (!documentIds.TryGetValue(full, out List<DocumentId>? ids))
            {
                ids = [];
                documentIds[full] = ids;
            }

            ids.Add(document.Id);
        }

        List<(string CsprojPath, string ProjectDirectory, string? EvaluatedOutputPath, string? IntermediateAssemblyPath)>
            projects = [];
        foreach (Project project in solution.Projects)
        {
            if (project.FilePath is null) continue;

            string projectFile = Path.GetFullPath(project.FilePath);
            string projectDirectory = Path.GetDirectoryName(projectFile)!;
            if (ProjectLanguages.SourceSuffixOf(project) is { } suffix) coneRoots.Add((projectDirectory, suffix));
            projects.Add((projectFile, projectDirectory, project.OutputFilePath, project.CompilationOutputInfo.AssemblyPath));
        }

        foreach (string path in ProjectCone.SolutionStructuralPaths(solutionPath, projects))
            RecordStructural(path);

        // A compiler the load read C# against is structural: an SDK updated in place can change the lag verdict.
        foreach (string compiler in compilers.Values.SelectMany(projectCompilers => projectCompilers))
            RecordStructural(compiler);

        foreach ((string projectDirectory, string suffix) in coneRoots)
        foreach (string coneFile in ProjectCone.Enumerate(projectDirectory, suffix))
            knownConeFiles.Add(coneFile);
    }

    private void RecordStructural(string path)
    {
        structuralFingerprints[Path.GetFullPath(path)] = FileFreshness.Capture(path);
    }

    /// <summary>
    ///     Pulls every document's text into a concrete in-memory <see cref="SourceText" />, anchoring the
    ///     reconcile baseline. MSBuildWorkspace backs documents with lazy file-text loaders, so an unforced
    ///     <see cref="TextDocument.GetTextAsync" /> during a later sweep would re-read disk, comparing on-disk
    ///     content against itself and masking every external edit. Returns the materialized solution and each
    ///     document path's stat, keyed by full path.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It runs inside the load, before anything there compiles, and each path is stat'ed before it is read,
    ///         as the reconcile sweep does, so a recorded stat is never newer than the text it vouches for and can
    ///         be trusted from the first sweep. A write between the stat and the read moves the stat and costs that
    ///         file one sweep read. Each path is read once and its text folded into every document there, the
    ///         several a multi-targeted project gives one file. A document with no file path is not on disk, so it
    ///         is left on its loader.
    ///     </para>
    ///     <para>
    ///         The reads overlap against the one immutable solution handed in, and only then are the texts folded
    ///         in by a run of <see cref="Solution.WithDocumentText(DocumentId,SourceText,PreservationMode)" />:
    ///         interleaving the two would make every read walk a solution one fork deeper than the last.
    ///     </para>
    /// </remarks>
    private static async Task<(Solution Solution, IReadOnlyDictionary<string, FileFreshness> StatsBeforeRead)>
        MaterializeDocumentTextsAsync(Solution solution, CancellationToken ct)
    {
        List<IGrouping<string, DocumentId>> paths = solution.Projects
            .SelectMany(project => project.Documents)
            .Where(document => document.FilePath is not null)
            .GroupBy(document => Path.GetFullPath(document.FilePath!), document => document.Id, PathComparison.Comparer)
            .ToList();

        Dictionary<string, FileFreshness> statsBeforeRead = paths.ToDictionary(
            group => group.Key,
            group => FileFreshness.Capture(group.Key),
            PathComparison.Comparer);

        List<Task<SourceText>> reads = paths
            .Select(group => solution.GetDocument(group.First())!.GetTextAsync(ct))
            .ToList();

        SourceText[] texts = await Task.WhenAll(reads).ConfigureAwait(false);

        Solution result = solution;
        for (var index = 0; index < paths.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            foreach (DocumentId id in paths[index])
                result = result.WithDocumentText(id, texts[index]);
        }

        return (result, statsBeforeRead);
    }

    private static async Task<SourceText> ReadTextAsync(string path, CancellationToken ct)
    {
        // Read via a stream so SourceText detects the encoding (BOM-aware, UTF-8 default), matching how
        // the workspace decoded the file on load. FileShare.ReadWrite tolerates a concurrent writer.
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        buffer.Position = 0;
        return SourceText.From(buffer);
    }

    private static bool PathEquals(string? left, string? right)
    {
        return string.Equals(left, right, PathComparison.Comparison);
    }
}
