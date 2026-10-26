using System.Runtime.CompilerServices;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Replay;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Roslyn.Replay;

namespace SlideRule.Cli.Pipeline;

/// <summary>
///     The MSBuildLocator quarantine. Neither <c>Program</c> nor the command wiring references a
///     Roslyn/MSBuild type, the request records it builds included; the command actions call in here.
///     <see cref="MethodImplOptions.NoInlining" /> keeps the JIT from resolving the runners (and through them
///     <c>MSBuildWorkspace</c>) until after registration and the source-selection decision have run. In tests
///     MSBuild is already registered by a <c>[ModuleInitializer]</c>, so
///     <see cref="MsBuildBootstrap.EnsureInitialized" /> is a no-op.
/// </summary>
/// <remarks>
///     <para>
///         <b>The host source.</b> Every entry point takes the <see cref="ISolutionSource" /> the cold branch is
///         served from: the source for a run that would otherwise open a fresh one-shot workspace, which a host
///         that already holds a solution supplies so its many invocations share that workspace. The replay
///         decision is upstream of it — an explicit <c>--binlog</c> or a valid capture still replays — so the
///         host source only ever replaces the <em>cold</em> branch it names.
///     </para>
///     <para>
///         <b>The environment seam.</b> The gate's discovery and the runner it dispatches to read through the one
///         <see cref="IEnvironment" /> every entry point takes, so the capture store and the fragment cache agree
///         on where the cache is, and the gate and the runner agree on which solution it is.
///     </para>
/// </remarks>
internal static class MsBuildGate
{
    private static readonly AsyncLocal<StrongBox<GateAcquisition?>?> AcquisitionOnThisFlow = new();

    /// <summary>
    ///     Starts recording, for this async flow and any task it starts, which source-selection branch a
    ///     <c>check</c>/<c>status</c>/<c>graph</c> run takes, and returns the box the gate writes the branch into.
    /// </summary>
    /// <remarks>
    ///     The box stays <c>null</c> for a verb that never reaches the decision. Install it before the call it
    ///     should record. Synchronous on purpose: a value an async method sets is discarded when it returns, so
    ///     only a synchronous installer leaves the box in place for its caller.
    /// </remarks>
    internal static StrongBox<GateAcquisition?> RecordAcquisitionOnThisFlow()
    {
        var box = new StrongBox<GateAcquisition?>();
        AcquisitionOnThisFlow.Value = box;
        return box;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunCheckAsync(
        CheckRequest request, TextWriter output, TextWriter error, ISolutionSource hostSource,
        IEnvironment environment, CancellationToken ct)
    {
        return SelectSourceAndRunAsync(
            request, error, hostSource, environment,
            (solution, source) =>
                InvokeCheckAsync(request with { Solution = solution }, output, error, source, environment, ct),
            ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunStatusAsync(
        StatusRequest request, TextWriter output, TextWriter error, ISolutionSource hostSource,
        IEnvironment environment, CancellationToken ct)
    {
        return SelectSourceAndRunAsync(
            request, error, hostSource, environment,
            (solution, source) =>
                InvokeStatusAsync(request with { Solution = solution }, output, error, source, environment, ct),
            ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunGraphAsync(
        GraphRequest request, TextWriter output, TextWriter error, ISolutionSource hostSource,
        IEnvironment environment, CancellationToken ct)
    {
        return SelectSourceAndRunAsync(
            request, error, hostSource, environment,
            (solution, source) =>
                InvokeGraphAsync(request with { Solution = solution }, output, error, source, environment, ct),
            ct);
    }

    // explain / render / baseline take the plain path: cache-aware, never replay-aware. None of them carries
    // --binlog, so there is no capture to validate and nothing for SelectSourceAndRunAsync to choose between;
    // a fragment-cache hit is decided down in CodebaseSource, where their --no-cache argument travels to.

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunExplainAsync(
        ExplainRequest request, TextWriter output, TextWriter error, ISolutionSource hostSource,
        IEnvironment environment, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return InvokeExplainAsync(request, output, error, hostSource, environment, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunRenderAsync(
        RenderRequest request, TextWriter output, TextWriter error, ISolutionSource hostSource,
        IEnvironment environment, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return InvokeRenderAsync(request, output, error, hostSource, environment, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunBaselineAsync(
        BaselineRequest request, TextWriter output, TextWriter error, ISolutionSource hostSource,
        IEnvironment environment, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return InvokeBaselineAsync(request, output, error, hostSource, environment, ct);
    }

    // Registration runs once up front: the binlog parser (MSBuild.StructuredLogger, under Basic.CompilerLog.Util)
    // binds Microsoft.Build.Framework through MSBuildLocator, so every branch — replay included — needs it, though
    // no replay branch opens an MSBuildWorkspace or runs a design-time build. The decision itself touches only
    // Replay-namespace + BCL types (no MSBuildWorkspace resolves here), and the runner types stay quarantined
    // behind the NoInlining stepping stones until after it.
    //
    // Every branch hands the runner the solution argument this decision settled on: the discovered absolute
    // file where the gate had to discover one to validate a capture, and the caller's own argument where it
    // did not. Downstream discovery over an explicit existing file returns it unchanged, so that spares the
    // run a second ancestor walk without changing what any of it resolves to.
    private static async Task<int> SelectSourceAndRunAsync(
        IReplayableRequest request, TextWriter error, ISolutionSource hostSource, IEnvironment environment,
        Func<string?, ISolutionSource, Task<int>> invokeRunner, CancellationToken ct)
    {
        EnsureMsBuildRegistered();

        string? binlog = request.Binlog;

        if (!string.IsNullOrWhiteSpace(binlog))
            return await RunExplicitBinlogAsync(request, binlog, environment, invokeRunner, ct);

        string? solution = request.Solution;
        if (!request.NoCache)
        {
            solution = ModelPipeline.DiscoverSolution(request.Solution, request.WorkingDirectory, environment);
            string? cacheRoot = environment.CacheRootOverride();
            CaptureValidation capture = ValidateCapture(solution, cacheRoot, ct);
            if (capture.State == CaptureState.Usable)
                return await RunCaptureReplayAsync(capture.BinlogCopyPath!, solution, error, hostSource, invokeRunner);
            if (capture.State == CaptureState.Invalid)
                return await RunNoticeColdAsync(capture.Notice!, solution, error, hostSource, invokeRunner);
        }

        return await RunColdAsync(solution, hostSource, invokeRunner);
    }

    // Explicit --binlog: replay the user's binlog (wrapping every failure loudly), then — unless --no-cache —
    // ingest it as a capture (refusals propagate as exit-2 UserErrorExceptions), then run over the replayed
    // solution. Eager, not lazy-in-source, so refusals and persistence fire deterministically even when the
    // fragment cache would hit and never acquire. The gate owns the replayed solution for the run's duration.
    private static async Task<int> RunExplicitBinlogAsync(
        IReplayableRequest request, string binlog, IEnvironment environment,
        Func<string?, ISolutionSource, Task<int>> invokeRunner, CancellationToken ct)
    {
        Record(GateAcquisition.ExplicitReplay);

        string binlogFullPath = Path.GetFullPath(binlog);
        if (!File.Exists(binlogFullPath))
            throw new UserErrorException(BinlogReplayMessages.MissingFileMessage(binlog));

        string solutionPath = ModelPipeline.DiscoverSolution(request.Solution, request.WorkingDirectory, environment);

        var diagnostics = new List<string>();
        using ReplayedSolution replayed = ReplayOrThrow(binlogFullPath, solutionPath, binlog, diagnostics, ct);
        if (!request.NoCache)
        {
            string? cacheRoot = environment.CacheRootOverride();
            new BinlogCaptureStore(solutionPath, cacheRoot).Ingest(replayed.Solution, binlogFullPath, binlog, ct);
        }

        var source = new ReplayedSolutionSource(
            replayed.Solution, replayed.LoadDiagnosticsWith(diagnostics), replayed.TargetFrameworks);
        return await invokeRunner(solutionPath, source);
    }

    // A structurally-valid capture, replayed lazily (LazyCaptureReplaySource). A runtime replay failure of the
    // validated copy is recoverable — acquisition precedes all rendering, so we print the notice and re-run cold
    // (MSBuildLocator is already registered from the up-front call); the two runner invocations build independent
    // runners and the failed one wrote nothing, so the retry cannot double-render.
    private static async Task<int> RunCaptureReplayAsync(
        string binlogCopyPath, string solutionPath, TextWriter error, ISolutionSource hostSource,
        Func<string?, ISolutionSource, Task<int>> invokeRunner)
    {
        Record(GateAcquisition.CaptureReplay);

        using var source = new LazyCaptureReplaySource(binlogCopyPath);
        try
        {
            return await invokeRunner(solutionPath, source);
        }
        catch (CaptureReplayFailedException ex)
        {
            await error.WriteLineAsync($"warning: {ex.Message}");
            Record(GateAcquisition.CaptureReplayFellBackToCold);
            return await invokeRunner(solutionPath, hostSource);
        }
    }

    // A stale/unreadable capture: run cold behind the capture's notice (NoticingSolutionSource).
    private static async Task<int> RunNoticeColdAsync(
        string notice, string solutionPath, TextWriter error, ISolutionSource hostSource,
        Func<string?, ISolutionSource, Task<int>> invokeRunner)
    {
        Record(GateAcquisition.NoticeCold);
        var source = new NoticingSolutionSource(notice, error, hostSource);
        return await invokeRunner(solutionPath, source);
    }

    // No capture and no explicit binlog (or --no-cache): the plain cold path, silent and byte-identical.
    private static async Task<int> RunColdAsync(
        string? solution, ISolutionSource hostSource, Func<string?, ISolutionSource, Task<int>> invokeRunner)
    {
        Record(GateAcquisition.Cold);
        return await invokeRunner(solution, hostSource);
    }

    // Writes into the box a caller installed on this flow, and nowhere else: a run with none installed — every
    // production run — records nothing, so no value outlives the run it describes.
    private static void Record(GateAcquisition acquisition)
    {
        if (AcquisitionOnThisFlow.Value is { } box) box.Value = acquisition;
    }

    private static ReplayedSolution ReplayOrThrow(
        string binlogFullPath, string solutionPath, string binlogArgument, List<string> diagnostics, CancellationToken ct)
    {
        try
        {
            return BinlogReplayer.Replay(binlogFullPath, solutionPath, diagnostics.Add, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UserErrorException(BinlogReplayMessages.ReplayFailedMessage(binlogArgument, ex.Message), ex);
        }
    }

    private static CaptureValidation ValidateCapture(string solutionPath, string? cacheRoot, CancellationToken ct)
    {
        try
        {
            return new BinlogCaptureStore(solutionPath, cacheRoot).Validate(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No resolvable cache location (or an unexpected store-construction failure): the silent cold path.
            return CaptureValidation.Absent();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureMsBuildRegistered()
    {
        MsBuildBootstrap.EnsureInitialized();
    }

    // The runner stepping stones. NoInlining: the runner and its workspace types resolve here, after the
    // registration and the source-selection decision have run.

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeCheckAsync(
        CheckRequest request, TextWriter output, TextWriter error, ISolutionSource source,
        IEnvironment environment, CancellationToken ct)
    {
        return new CheckRunner(output, error, source, environment).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeStatusAsync(
        StatusRequest request, TextWriter output, TextWriter error, ISolutionSource source,
        IEnvironment environment, CancellationToken ct)
    {
        return new StatusRunner(output, error, source, environment).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeGraphAsync(
        GraphRequest request, TextWriter output, TextWriter error, ISolutionSource source,
        IEnvironment environment, CancellationToken ct)
    {
        return new GraphRunner(output, error, source, environment).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeExplainAsync(
        ExplainRequest request, TextWriter output, TextWriter error, ISolutionSource source,
        IEnvironment environment, CancellationToken ct)
    {
        return new ExplainRunner(output, error, source, environment).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeRenderAsync(
        RenderRequest request, TextWriter output, TextWriter error, ISolutionSource source,
        IEnvironment environment, CancellationToken ct)
    {
        return new RenderRunner(output, error, source, environment).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeBaselineAsync(
        BaselineRequest request, TextWriter output, TextWriter error, ISolutionSource source,
        IEnvironment environment, CancellationToken ct)
    {
        return new BaselineRunner(output, error, source, environment).RunAsync(request, ct);
    }
}
