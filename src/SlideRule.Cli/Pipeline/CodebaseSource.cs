using Microsoft.CodeAnalysis;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.SpecLoading;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Caching;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Sessions;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Cli.Pipeline;

/// <summary>
///     How a <see cref="CodebaseSource" /> produced its model — the internal test observable for the
///     persisted extraction cache. Never printed: stdout/stderr stay byte-identical to a cold
///     run in every mode, so this only ever tells a test which path a run took.
/// </summary>
internal enum CodebaseSourceOutcome
{
    /// <summary>Every project was clean: the model was merged from cached fragments with no workspace opened.</summary>
    Hit,

    /// <summary>Some projects were dirty: the clean fragments were reused, only the dirty ones re-extracted.</summary>
    Partial,

    /// <summary>No usable cache: the whole model was extracted from a freshly loaded workspace and written back.</summary>
    Miss,

    /// <summary>
    ///     The cache was bypassed entirely (<c>--no-cache</c>, or no cache location) — a pure cold run, no read, no
    ///     write.
    /// </summary>
    Disabled
}

/// <summary>
///     The one model-acquisition seam a run reads this solution through. It carries what a run needs before the
///     codebase itself and defers the codebase to a lazy <see cref="ExtractAsync" />, so the pipeline can still
///     fail fast on a tampered baseline before any extraction. A workspace, where the run still needs one, is
///     acquired through the injected <see cref="ISolutionSource" />.
/// </summary>
/// <remarks>
///     The persisted cache is disposable local derived data with no tamper story: a read only ever hits when
///     the store has validated every input against disk, a fragment merge is the one code path a cold run
///     also takes (so the model is identical by construction), and every write is best-effort — any doubt or
///     failure degrades to a plain cold extraction, never a wrong answer.
/// </remarks>
internal sealed class CodebaseSource : IDisposable
{
    private readonly CacheReadResult cacheRead;

    // The solution's declared membership, read at most once per source and handed to every extraction path so
    // each project it collects carries ProjectNode.SolutionMember. Resolution needs the same set (see
    // SpecResolver.Resolve), so the cold spec path forces its own reader and hands it in rather than reading
    // twice; every other path mints one here and leaves it unforced. Lazy because a cache hit merges stored
    // fragments and never extracts, and the point of a hit is that it touches nothing it does not have to.
    private readonly Lazy<IReadOnlySet<string>?> declaredMembers;

    private readonly SolutionHandle? handle;
    private readonly WorkspaceDiagnostics loadDiagnostics;

    // The models merged from the walked fragments, keyed by exclusion. Two tiers because a walk costs three
    // orders of magnitude more than a merge, which is CPU over fragments already in hand. Plain, unlocked
    // collections: a source belongs to one run and its ExtractAsync is never re-entered concurrently, unlike
    // the session-lifetime store the warm path uses.
    private readonly Dictionary<string, CodebaseModel> mergedByExclusion = new(StringComparer.Ordinal);

    private readonly ArchitectureModel? model;
    private readonly string normalizedSpecArgument;
    private readonly SpecResolution? resolution;
    private readonly ExtractionCacheStore? store;

    // The fragment set this source acquired, held for its lifetime — walked from the workspace, or taken
    // whole from a cache hit. Null until the first ExtractAsync, and written exactly once after it.
    private IReadOnlyList<CodebaseFragment>? fragments;

    // The load's verdict with the merge notes (same-FQN cross-project conflation) and multi-targeted projects of
    // the last ExtractAsync, regenerated from the fragments on every path — a cache hit re-merges, so these need
    // no persistence. Both are empty until ExtractAsync runs.

    private HashSet<string> reExtractedProjects = new(StringComparer.Ordinal);

    private CodebaseSource(
        CodebaseSourceOutcome outcome,
        string solutionPath,
        WorkspaceDiagnostics loadDiagnostics,
        ArchitectureModel? model,
        SpecResolution? resolution,
        SolutionHandle? handle,
        ExtractionCacheStore? store,
        CacheReadResult cacheRead,
        string normalizedSpecArgument,
        Lazy<IReadOnlySet<string>?>? declaredMembers = null)
    {
        Outcome = outcome;
        SolutionPath = solutionPath;
        SolutionName = Path.GetFileName(solutionPath);
        SolutionDirectory = SolutionProjectFileParser.AnchorDirectory(solutionPath);
        this.declaredMembers = declaredMembers
                               ?? new Lazy<IReadOnlySet<string>?>(() => SpecExclusion.TryReadDeclaredMembers(solutionPath));
        this.loadDiagnostics = loadDiagnostics;
        Diagnostics = loadDiagnostics;
        this.model = model;
        this.resolution = resolution;
        this.handle = handle;
        this.store = store;
        this.cacheRead = cacheRead;
        this.normalizedSpecArgument = normalizedSpecArgument;
    }

    /// <summary>Which path this run took.</summary>
    internal CodebaseSourceOutcome Outcome { get; }

    /// <summary>The finalized architecture model. Absent for the spec-less <c>graph</c> survey.</summary>
    public ArchitectureModel Model =>
        model ?? throw new InvalidOperationException("This codebase source was created without a spec (graph is spec-less).");

    /// <summary>The resolved spec (DLL + excluded projects). Absent for the spec-less <c>graph</c> survey.</summary>
    public SpecResolution Resolution =>
        resolution ?? throw new InvalidOperationException("This codebase source was created without a spec (graph is spec-less).");

    /// <summary>
    ///     How well the workspace loaded (collected on a cold run, replayed from the cache on a hit), with the merge
    ///     notes and multi-targeted projects of the last <see cref="ExtractAsync" />, both empty before it runs, so
    ///     a verb that renders after extracting sees them and one that gates before it does not have to wait for
    ///     them.
    /// </summary>
    public WorkspaceDiagnostics Diagnostics { get; private set; }

    /// <summary>
    ///     Absolute path to the discovered <c>.sln</c>/<c>.slnx</c>, or to the <c>.slnf</c> filtering one.
    /// </summary>
    public string SolutionPath { get; }

    /// <summary>
    ///     The solution's file name, the machine-independent name every document gives the run's subject; no
    ///     document carries <see cref="SolutionPath" /> itself. Under a <c>.slnf</c> this is the filter's own
    ///     name, unlike <see cref="SolutionDirectory" />: the filter is what the run was pointed at, and naming it
    ///     is how a reader learns the answer covers a lens rather than the whole solution.
    /// </summary>
    public string SolutionName { get; }

    /// <summary>
    ///     The directory the solution's conventions anchor at: baselines, the context files and model file
    ///     <c>render</c> writes, diff resolution, <c>context --path</c>, and every evidence path a document
    ///     relativizes. Under a <c>.slnf</c> that is the directory of the solution the filter <em>references</em>,
    ///     not the filter's own: a filter is a lens on a solution, so a run through one must resolve the same
    ///     conventions and write to the same places as a run over the solution itself.
    /// </summary>
    // Assigned once in the ctor, never computed per read: under a .slnf the computation costs a file read.
    public string SolutionDirectory { get; }

    /// <summary>
    ///     The projects extracted from the workspace on this run (all of them on a miss, only the dirty set
    ///     on a partial, the session store's re-walked set on the warm path, none on a hit or a cold disabled
    ///     run).
    /// </summary>
    internal IReadOnlySet<string> ReExtractedProjects => reExtractedProjects;

    /// <summary>
    ///     How many times this source walked the workspace for fragments — at most once for its whole
    ///     lifetime, however many exclusion sets its callers ask for, and zero on a cache hit or the warm
    ///     path (neither walks).
    /// </summary>
    internal int ExtractionCount { get; private set; }

    /// <summary>Disposes the owned cold workspace; a no-op on a cache hit (which owns none).</summary>
    public void Dispose()
    {
        handle?.Dispose();
    }

    /// <summary>
    ///     Discovers the solution and prepares a spec-ful source: on a cache hit the spec is replayed and the
    ///     model loaded with no workspace; otherwise a workspace is acquired and the spec resolved against it.
    ///     Discovery, spec-resolution, and spec-load failures surface exactly as a cold run raises them.
    /// </summary>
    /// <remarks>
    ///     A run with <paramref name="noCache" /> still acquires through <paramref name="source" />, so a host
    ///     keeping a warm session serves it that session's model (<see cref="SolutionHandle.WarmCodebase" />)
    ///     rather than a fresh walk per call.
    /// </remarks>
    public static async Task<CodebaseSource> CreateWithSpecAsync(
        ISolutionSource source,
        IEnvironment environment,
        string? solution,
        string? spec,
        string workingDirectory,
        bool noCache,
        CancellationToken ct)
    {
        string solutionPath = ModelPipeline.DiscoverSolution(solution, workingDirectory, environment);
        ExtractionCacheStore? store = noCache ? null : TryCreateStore(solutionPath, environment);
        string normalized = NormalizeSpecArgument(spec);

        // A null store is a Disabled run: nothing read from cache.json and nothing written to it, which is what
        // --no-cache and every cache-free caller mean.
        if (store is null)
            return await CreateColdWithSpecAsync(
                source, solutionPath, spec, normalized, null, CacheReadResult.Miss(),
                CodebaseSourceOutcome.Disabled, ct);

        CacheReadResult read = store.ReadAndValidate(ct);
        if (read.Outcome == CacheOutcome.Hit
            && ResolveSpecOnHit(spec, read.SpecResolutions) is { } hitResolution)
        {
            ArchitectureModel hitModel = source.LoadSpecModel(hitResolution.DllPath);
            return new CodebaseSource(
                CodebaseSourceOutcome.Hit, solutionPath, read.LoadDiagnostics, hitModel, hitResolution, null,
                store, read, normalized);
        }

        // A miss, a partial, or a hit whose spec was not recorded: acquire the workspace and resolve cold.
        CodebaseSourceOutcome coldOutcome = ColdOutcomeFor(read.Outcome);
        return await CreateColdWithSpecAsync(source, solutionPath, spec, normalized, store, read, coldOutcome, ct);
    }

    /// <summary>
    ///     Discovers the solution and prepares a spec-less source for <c>graph</c>: a cache hit merges every
    ///     cached fragment with no workspace; otherwise a workspace is acquired and the whole codebase extracted.
    /// </summary>
    public static async Task<CodebaseSource> CreateSpeclessAsync(
        ISolutionSource source,
        IEnvironment environment,
        string? solution,
        string workingDirectory,
        bool noCache,
        CancellationToken ct)
    {
        string solutionPath = ModelPipeline.DiscoverSolution(solution, workingDirectory, environment);
        ExtractionCacheStore? store = noCache ? null : TryCreateStore(solutionPath, environment);

        if (store is null)
            return await CreateColdSpeclessAsync(
                source, solutionPath, null, CacheReadResult.Miss(), CodebaseSourceOutcome.Disabled, ct);

        CacheReadResult read = store.ReadAndValidate(ct);
        if (read.Outcome == CacheOutcome.Hit)
            return new CodebaseSource(
                CodebaseSourceOutcome.Hit, solutionPath, read.LoadDiagnostics, null, null, null, store, read, "");

        CodebaseSourceOutcome coldOutcome = ColdOutcomeFor(read.Outcome);
        return await CreateColdSpeclessAsync(source, solutionPath, store, read, coldOutcome, ct);
    }

    /// <summary>
    ///     Produces the codebase model, excluding <paramref name="excludeProjectNames" /> (the spec project
    ///     and its private plumbing, or empty for <c>graph</c>).
    /// </summary>
    /// <remarks>
    ///     Callable more than once per source, and cheap after the first: the walk (and, with a store, the
    ///     fingerprint and the write-back) happen on the first call alone, and a repeat with an exclusion set
    ///     already merged hands back that model rather than rebuilding it. A repeat with a <em>different</em>
    ///     exclusion set still merges — it is a different model — but off the fragments already in hand.
    ///     The two facts below are re-read per call regardless, because they differ per
    ///     exclusion: which same-FQN types conflated, and which projects arrived multi-targeted.
    /// </remarks>
    public async Task<CodebaseModel> ExtractAsync(IReadOnlyCollection<string> excludeProjectNames, CancellationToken ct)
    {
        CodebaseModel codebase = await ExtractCoreAsync(excludeProjectNames, ct);
        // Captured here, off the one exit every path (hit, warm, cold) returns through, so Diagnostics can
        // surface the notes beside the workspace-load failures.
        Diagnostics = loadDiagnostics.WithModel(codebase);
        return codebase;
    }

    private async Task<CodebaseModel> ExtractCoreAsync(
        IReadOnlyCollection<string> excludeProjectNames, CancellationToken ct)
    {
        if (WarmCodebaseProducer() is { } warmCodebase)
        {
            // The warm MCP path: the session store (SessionFragmentStore) produces the model, and its
            // re-extraction set becomes this source's observable so the runner counters keep meaning.
            //
            // It returns above this source's own memo rather than through it, deliberately: the store must
            // be re-consulted per call so an edit between two calls is re-walked, and a memo here would
            // answer the second call from the first call's model and strand the edit.
            SessionCodebase warm = await warmCodebase(excludeProjectNames, declaredMembers.Value, ct);
            reExtractedProjects = new HashSet<string>(warm.ReExtractedProjects, StringComparer.Ordinal);
            return warm.Model;
        }

        fragments ??= await WalkFragmentsAsync(ct);
        return MergedFor(fragments, excludeProjectNames);
    }

    // The warm path's producer when this run takes it: a cold-loaded handle carrying a session codebase, and
    // no persisted store to prefer over it. Null whenever a persisted store is live, which every hit has, since
    // the store path serves every caller from the fragments it also writes back.
    private Func<IReadOnlyCollection<string>, IReadOnlySet<string>?, CancellationToken, Task<SessionCodebase>>?
        WarmCodebaseProducer()
    {
        return store is null ? handle!.WarmCodebase : null;
    }

    private async Task<IReadOnlyList<CodebaseFragment>> WalkFragmentsAsync(CancellationToken ct)
    {
        if (Outcome == CodebaseSourceOutcome.Hit) return cacheRead.ReusableFragments;

        ExtractionCount++;
        Solution solution = handle!.Solution;

        if (store is null)
            // Disabled and cold: walk every project and let the merge above drop what each caller excludes.
            // That is byte-identical to excluding at collection — FragmentMerger.Retain is an order-preserving
            // filter on ProjectName, over a list CollectInputsAsync has already ordered — and it is what lets
            // one walk serve two exclusion sets. reExtractedProjects stays empty on this cold disabled run:
            // the field says which projects a cache, persisted or session, made this run re-extract.
            return await CodebaseExtractor.ExtractFragmentsAsync(
                solution, null, handle.TargetFrameworks, handle.ProjectFiles, declaredMembers.Value, ct);

        // Fingerprint before extraction so a mid-run edit is caught by the store's re-stat at write time.
        CacheFingerprint? fingerprint = TryCaptureFingerprint(solution, ct);

        List<CodebaseFragment> allFragments = await ExtractAllFragmentsAsync(solution, ct);
        if (fingerprint is not null)
            TryWrite(fingerprint, allFragments, ct);

        return allFragments;
    }

    // The merged model for one exclusion set, memoized against the walked fragments. Keyed through the
    // merger's own ExclusionKey, so this memo and the warm session store's cannot disagree about when two
    // callers are asking for the same merge. Sharing the instance is safe because a merged model is read-only
    // once built.
    private CodebaseModel MergedFor(
        IReadOnlyList<CodebaseFragment> walked, IReadOnlyCollection<string> excludeProjectNames)
    {
        string key = FragmentMerger.ExclusionKey(excludeProjectNames);
        if (mergedByExclusion.TryGetValue(key, out CodebaseModel? memoized)) return memoized;

        CodebaseModel merged = FragmentMerger.Merge(FragmentMerger.Retain(walked, excludeProjectNames));
        mergedByExclusion[key] = merged;
        return merged;
    }

    // The full fragment set in ordinal project order on either path, so the merge and the write-back order match
    // a cold run exactly.
    private async Task<List<CodebaseFragment>> ExtractAllFragmentsAsync(Solution solution, CancellationToken ct)
    {
        if (Outcome == CodebaseSourceOutcome.Partial)
        {
            IReadOnlyList<CodebaseFragment> reExtracted = await CodebaseExtractor.ExtractFragmentsAsync(
                solution, cacheRead.DirtyProjects, handle!.TargetFrameworks, handle.ProjectFiles,
                declaredMembers.Value, ct);
            reExtractedProjects = new HashSet<string>(cacheRead.DirtyProjects, StringComparer.Ordinal);
            return cacheRead.ReusableFragments
                .Concat(reExtracted)
                .OrderBy(f => f.ProjectName, StringComparer.Ordinal)
                .ToList();
        }

        List<CodebaseFragment> all =
        [
            .. await CodebaseExtractor.ExtractFragmentsAsync(
                solution, null, handle!.TargetFrameworks, handle.ProjectFiles, declaredMembers.Value, ct)
        ];
        reExtractedProjects = all.Select(f => f.ProjectName).ToHashSet(StringComparer.Ordinal);
        return all;
    }

    private static async Task<CodebaseSource> CreateColdWithSpecAsync(
        ISolutionSource source, string solutionPath, string? spec, string normalizedSpec,
        ExtractionCacheStore? store, CacheReadResult cacheRead, CodebaseSourceOutcome outcome, CancellationToken ct)
    {
        // A partial resolves its spec from the record a hit replays, because resolving it cold scans the whole
        // solution. Only then can it load just its dirty projects' closure.
        SpecResolution? replayed = outcome == CodebaseSourceOutcome.Partial
            ? ResolveSpecOnHit(spec, cacheRead.SpecResolutions)
            : null;
        SolutionHandle handle = await AcquireForRunAsync(source, solutionPath, cacheRead, replayed is not null, ct);
        try
        {
            if (handle.IsClosure)
            {
                ArchitectureModel closureModel = source.LoadSpecModel(replayed!.DllPath);
                return new CodebaseSource(
                    outcome, solutionPath, cacheRead.LoadDiagnostics, closureModel, replayed, handle, store, cacheRead,
                    normalizedSpec);
            }

            SessionSpecResolution resolved = ResolveSpec(handle, solutionPath, spec, normalizedSpec);
            ArchitectureModel model = source.LoadSpecModel(resolved.Resolution.DllPath);
            // The membership set travels on as the one resolution read (or replayed), so what resolution
            // saw is what extraction gets — already computed, hence the value-taking Lazy.
            return new CodebaseSource(
                outcome, solutionPath, handle.LoadDiagnostics, model, resolved.Resolution, handle, store, cacheRead,
                normalizedSpec, new Lazy<IReadOnlySet<string>?>(resolved.DeclaredMembers));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Resolves the spec against the acquired handle, through the session's memo where the handle
    ///     carries one (<see cref="SolutionHandle.WarmSpecResolution" />) and cold where it does not.
    /// </summary>
    /// <remarks>
    ///     A prebuilt-DLL <c>--spec</c> is answered first, before the memo is consulted at all — exactly as
    ///     <see cref="ResolveSpecOnHit" /> answers it first. That branch needs no workspace and no walk, so
    ///     there is nothing for a cache to spare; and replaying it would refuse in the built-output search's
    ///     words, which name a spec project a DLL spec does not have.
    /// </remarks>
    private static SessionSpecResolution ResolveSpec(
        SolutionHandle handle, string solutionPath, string? spec, string normalizedSpec)
    {
        if (SpecResolver.TryResolveWithoutSolution(spec) is { } dllResolution)
            return new SessionSpecResolution(dllResolution, SpecExclusion.TryReadDeclaredMembers(solutionPath));

        return handle.WarmSpecResolution is { } warm
            ? warm(normalizedSpec, ResolveFully)
            : ResolveFully();

        // The cold resolution, and the only reader of the solution's declared membership on this path: both
        // halves need the same set, so it is read once here and travels out with the resolution.
        SessionSpecResolution ResolveFully()
        {
            IReadOnlySet<string>? declaredMembers = SpecExclusion.TryReadDeclaredMembers(solutionPath);
            SpecResolution resolution = SpecResolver.Resolve(
                handle.Solution, handle.ProjectFiles, declaredMembers, spec, handle.LoadDiagnostics);
            return new SessionSpecResolution(resolution, declaredMembers);
        }
    }

    private static async Task<CodebaseSource> CreateColdSpeclessAsync(
        ISolutionSource source, string solutionPath, ExtractionCacheStore? store, CacheReadResult cacheRead,
        CodebaseSourceOutcome outcome, CancellationToken ct)
    {
        SolutionHandle handle = await AcquireForRunAsync(
            source, solutionPath, cacheRead, outcome == CodebaseSourceOutcome.Partial, ct);
        WorkspaceDiagnostics loadDiagnostics = handle.IsClosure ? cacheRead.LoadDiagnostics : handle.LoadDiagnostics;
        return new CodebaseSource(outcome, solutionPath, loadDiagnostics, null, null, handle, store, cacheRead, "");
    }

    /// <summary>
    ///     The workspace a run that cannot be served from the cache alone opens. A partial that may load only its
    ///     dirty projects' closure asks for it; any other run, and a closure that cannot stand in for the whole
    ///     solution, gets the whole solution.
    /// </summary>
    /// <remarks>
    ///     A closure reports the recorded load verdict, the one a hit replays, because a partial is never a
    ///     structural change: the projects it did not load are the projects the last whole load measured. What
    ///     the closure measured itself has to agree with that record (<see cref="ClosureServes" />), or the run
    ///     loads the whole solution and reports what it finds there.
    /// </remarks>
    private static async Task<SolutionHandle> AcquireForRunAsync(
        ISolutionSource source, string solutionPath, CacheReadResult cacheRead, bool closureAllowed, CancellationToken ct)
    {
        if (!closureAllowed || cacheRead.Validated is not { } validated) return await source.AcquireAsync(solutionPath, ct);

        SolutionHandle closure = await source.AcquireClosureAsync(solutionPath, validated.DirtyProjectFiles, ct);
        if (!closure.IsClosure || ClosureServes(closure, cacheRead)) return closure;

        closure.Dispose();
        return await source.AcquireAsync(solutionPath, ct);
    }

    private static bool ClosureServes(SolutionHandle closure, CacheReadResult cacheRead)
    {
        WorkspaceDiagnostics measured = closure.LoadDiagnostics;
        WorkspaceDiagnostics recorded = cacheRead.LoadDiagnostics;
        HashSet<string> loaded = closure.Solution.Projects
            .Select(project => project.Name)
            .ToHashSet(StringComparer.Ordinal);
        var recordedRestoreFailures = new HashSet<string>(recorded.RestoreFailedProjects, PathComparison.Comparer);
        var recordedMessages = new HashSet<string>(recorded.LoadFailures, StringComparer.Ordinal);

        return cacheRead.DirtyProjects.All(loaded.Contains)
               && measured.FailedProjects.Count == 0
               && measured.RestoreFailedProjects.All(recordedRestoreFailures.Contains)
               && measured.LoadFailures.All(recordedMessages.Contains);
    }

    /// <summary>
    ///     Resolves the spec on a cache hit without a workspace: an explicit DLL directly, a convention or csproj
    ///     spec by replaying its recorded resolution, and null when no record matches, so the caller reloads the
    ///     workspace. A missing DLL throws the same loud error a cold run would, and a replay re-runs the
    ///     built-output check, so the search and its error text match cold.
    /// </summary>
    /// <remarks>
    ///     The recorded intermediate assembly path is replayed rather than left null, because that is what
    ///     makes the search's refusal of an intermediate result identical on both paths: without it a hit
    ///     would answer with the <c>obj</c>-side assembly a cold run refuses. That whole live half runs
    ///     through <see cref="SpecResolver.Replay" />, the one owner the warm session's
    ///     <see cref="SpecResolutionCache" /> replays through too.
    /// </remarks>
    internal static SpecResolution? ResolveSpecOnHit(string? spec, IReadOnlyList<SpecResolutionRecord> records)
    {
        if (SpecResolver.TryResolveWithoutSolution(spec) is { } dllResolution) return dllResolution;

        string normalized = NormalizeSpecArgument(spec);
        SpecResolutionRecord? record = records.FirstOrDefault(r => string.Equals(r.NormalizedSpecArgument, normalized, StringComparison.Ordinal));
        if (record is null) return null;

        return SpecResolver.Replay(
            record.SpecProjectName, normalized, record.ExcludeProjectNames, record.OutputFilePaths,
            record.IntermediateAssemblyPath);
    }

    private CacheFingerprint? TryCaptureFingerprint(Solution solution, CancellationToken ct)
    {
        try
        {
            // On a partial the validation's stamps ride in, so only the dirty projects' changed documents are read
            // again; on a miss there are none and every input is stamped. A closure holds only some projects, so
            // it writes back only by carrying the rest.
            IReadOnlyList<ProjectInputs> inputs = SolutionCacheInputs.Collect(solution);
            return handle!.IsClosure
                ? store!.TryCaptureOverValidation(inputs, cacheRead.Validated, ct)
                : store!.CaptureFingerprint(inputs, cacheRead.Validated, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // could not fingerprint ⇒ skip the write and keep the correct, freshly extracted model
        }
    }

    private void TryWrite(CacheFingerprint fingerprint, IReadOnlyList<CodebaseFragment> allFragments, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<SpecResolutionRecord> records = BuildWriteSpecRecords();
            store!.Write(fingerprint, new ExtractionResult(allFragments, records, loadDiagnostics), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The cache is disposable derived data: a write failure must never change the run's result.
        }
    }

    // The spec record to persist for this run, unioned onto the manifest's existing records (replacing any
    // with the same normalized argument). Only a solution-member spec (convention/csproj — it names a spec
    // project) is recorded, since that is the resolution a hit cannot replay without one; an explicit DLL
    // resolves on a hit with no record, and graph has no spec at all. The whole excluded set rides along:
    // the hit path has no workspace to re-walk the spec project's ProjectReference closure with.
    private IReadOnlyList<SpecResolutionRecord> BuildWriteSpecRecords()
    {
        IReadOnlyList<SpecResolutionRecord> existing = cacheRead.SpecResolutions;
        if (resolution?.SpecProjectName is not { } specProjectName) return existing;

        // The built-output inputs come off the resolution that consumed them rather than being re-derived
        // here. Re-deriving could only group the spec project's Roslyn projects by NAME, while resolution
        // groups them by canonicalized project file — the distinction that keeps two same-named csprojs from
        // recording each other's outputs. Both slots are non-null on this branch: naming a spec project is
        // exactly what the solution-member resolution does, and that is the branch that fills them.
        IReadOnlyList<string> outputFilePaths = resolution.OutputFilePaths ?? [];

        var record = new SpecResolutionRecord(
            normalizedSpecArgument, specProjectName, [.. resolution.ExcludeProjectNames], outputFilePaths,
            resolution.IntermediateAssemblyPath);
        return existing
            .Where(r => !string.Equals(r.NormalizedSpecArgument, normalizedSpecArgument, StringComparison.Ordinal))
            .Append(record)
            .ToList();
    }

    // What a run that has to open a workspace calls itself; a hit the spec could not replay counts as a miss.
    private static CodebaseSourceOutcome ColdOutcomeFor(CacheOutcome outcome)
    {
        return outcome == CacheOutcome.Partial ? CodebaseSourceOutcome.Partial : CodebaseSourceOutcome.Miss;
    }

    private static string NormalizeSpecArgument(string? spec)
    {
        return string.IsNullOrWhiteSpace(spec) ? "" : Path.GetFullPath(spec);
    }

    private static ExtractionCacheStore? TryCreateStore(string solutionPath, IEnvironment environment)
    {
        try
        {
            string? cacheRoot = environment.CacheRootOverride();
            return new ExtractionCacheStore(solutionPath, cacheRoot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // no resolvable cache location ⇒ run cold, no read and no write
        }
    }
}
