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
///     Roslyn/MSBuild type; the command actions call in here. <see cref="MethodImplOptions.NoInlining" />
///     keeps the JIT from resolving the runners (and through them <c>MSBuildWorkspace</c>) until after
///     registration and the source-selection decision have run. In tests MSBuild is already registered by a
///     <c>[ModuleInitializer]</c>, so <see cref="MsBuildBootstrap.EnsureInitialized" /> is a no-op.
/// </summary>
/// <remarks>
///     <para>
///         <b>The host source.</b> Every entry point takes an optional <see cref="ISolutionSource" />: the
///         source to serve a run that would otherwise open a fresh one-shot workspace. <c>null</c> — what
///         <c>Program</c> passes, and therefore what every real CLI invocation uses — means
///         <see cref="ColdSolutionSource" />, so the seam costs a real CLI invocation nothing. A host that
///         already holds a solution supplies its own: the MCP server does it through DI on the runners it
///         calls directly, and the in-process e2e harness does it here, so a whole test class's CLI
///         invocations share one loaded workspace instead of opening one each. The replay decision is
///         upstream of it — an explicit <c>--binlog</c> or a valid capture still replays — so the host
///         source only ever replaces the <em>cold</em> branch it names.
///     </para>
///     <para>
///         <b>The environment seam.</b> Every entry point also takes an optional
///         <see cref="IEnvironment" />, threaded from <see cref="CliEntry" /> so a host can supply the
///         cache-root override and the solution variable without touching real process state. <c>null</c> —
///         what <c>Program</c> passes — means <see cref="SystemEnvironment" />, so production reads the real
///         process variables.
///     </para>
/// </remarks>
internal static class MsBuildGate
{
    private static readonly AsyncLocal<StrongBox<GateAcquisition?>?> AcquisitionOnThisFlow = new();

    /// <summary>
    ///     Starts recording, for this async flow and any task it starts, which source-selection branch a
    ///     <c>check</c>/<c>status</c>/<c>graph</c> run takes, and returns the box the gate writes the branch into;
    ///     the box stays <c>null</c> for a verb that never reaches the decision. Install it before the call it should record.
    ///     Synchronous on purpose: a value an async method sets is discarded when it returns, so only a
    ///     synchronous installer leaves the box in place for its caller. Internal test observable — output is
    ///     byte-identical whichever branch runs, so this only tells a test which path was chosen; never
    ///     printed, and a run with no box installed records nothing.
    /// </summary>
    internal static StrongBox<GateAcquisition?> RecordAcquisitionOnThisFlow()
    {
        var box = new StrongBox<GateAcquisition?>();
        AcquisitionOnThisFlow.Value = box;
        return box;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunCheckAsync(
        CheckRequest request, TextWriter output, TextWriter error, ISolutionSource? hostSource,
        IEnvironment? environment, CancellationToken ct)
    {
        return SelectSourceAndRunAsync(
            request, error, hostSource, environment,
            (solution, source) =>
                InvokeCheckAsync(request with { Solution = solution }, output, error, source, environment, ct),
            ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunStatusAsync(
        StatusRequest request, TextWriter output, TextWriter error, ISolutionSource? hostSource,
        IEnvironment? environment, CancellationToken ct)
    {
        return SelectSourceAndRunAsync(
            request, error, hostSource, environment,
            (solution, source) =>
                InvokeStatusAsync(request with { Solution = solution }, output, error, source, environment, ct),
            ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunGraphAsync(
        GraphRequest request, TextWriter output, TextWriter error, ISolutionSource? hostSource,
        IEnvironment? environment, CancellationToken ct)
    {
        return SelectSourceAndRunAsync(
            request, error, hostSource, environment,
            (solution, source) =>
                InvokeGraphAsync(request with { Solution = solution }, output, error, source, environment, ct),
            ct);
    }

    // explain / render / baseline take the plain path: cache-aware, never replay-aware.
    //
    // These three front the persisted extraction cache, and so take the environment seam the cache root is
    // read through, exactly as the three above do. What they do NOT take is the replay
    // decision: none of them carries --binlog, so there is no capture to validate and nothing for
    // SelectSourceAndRunAsync to choose between — a fragment-cache hit is decided down in CodebaseSource,
    // where their --no-cache argument travels to.

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunExplainAsync(
        ExplainRequest request, TextWriter output, TextWriter error, ISolutionSource? hostSource,
        IEnvironment? environment, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return InvokeExplainAsync(request, output, error, SourceOrCold(hostSource), environment, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunRenderAsync(
        RenderRequest request, TextWriter output, TextWriter error, ISolutionSource? hostSource,
        IEnvironment? environment, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return InvokeRenderAsync(request, output, error, SourceOrCold(hostSource), environment, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunBaselineAsync(
        BaselineRequest request, TextWriter output, TextWriter error, ISolutionSource? hostSource,
        IEnvironment? environment, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return InvokeBaselineAsync(request, output, error, SourceOrCold(hostSource), environment, ct);
    }

    // The pre-decision: an explicit --binlog wins (replay-first, eagerly); otherwise, unless --no-cache, a
    // structurally-valid capture replays lazily, a stale/unreadable one prints its notice and falls back to a
    // design-time build, and an absent one runs the plain cold path. Registration runs once up front: the
    // binlog parser (MSBuild.StructuredLogger, under Basic.CompilerLog.Util) binds Microsoft.Build.Framework
    // through MSBuildLocator, so every branch — replay included — needs it, though no replay branch opens an
    // MSBuildWorkspace or runs a design-time build. The decision itself touches only Replay-namespace + BCL
    // types (no MSBuildWorkspace resolves here), and the runner types stay quarantined behind the NoInlining
    // stepping stones until after it.
    //
    // Every branch hands the runner the solution argument this decision settled on: the discovered absolute
    // file where the gate had to discover one to validate a capture, and the caller's own argument where it
    // did not. Downstream discovery over an explicit existing file returns it unchanged, so that spares the
    // run a second ancestor walk without changing what any of it resolves to.
    private static async Task<int> SelectSourceAndRunAsync(
        IReplayableRequest request, TextWriter error, ISolutionSource? hostSource, IEnvironment? environment,
        Func<string?, ISolutionSource, Task<int>> invokeRunner, CancellationToken ct)
    {
        EnsureMsBuildRegistered();

        // The gate reads the cache root and the solution variable through the same seam the runners it
        // dispatches to use, so a caller that supplies one gets a run whose capture store and fragment cache agree
        // on where the cache is, and whose discovery here agrees with theirs on which solution it is.
        IEnvironment seam = environment ?? new SystemEnvironment();
        string? binlog = request.Binlog;

        if (!string.IsNullOrWhiteSpace(binlog))
            return await RunExplicitBinlogAsync(request, binlog, seam, invokeRunner, ct);

        string? solution = request.Solution;
        if (!request.NoCache)
        {
            solution = ModelPipeline.DiscoverSolution(request.Solution, request.WorkingDirectory, seam);
            string? cacheRoot = seam.CacheRootOverride();
            CaptureValidation capture = ValidateCapture(solution, cacheRoot, ct);
            if (capture.State == CaptureState.Usable)
                return await RunCaptureReplayAsync(capture.BinlogCopyPath!, solution, error, hostSource, invokeRunner);
            if (capture.State == CaptureState.Invalid)
                return await RunNoticeColdAsync(capture.Notice!, solution, error, hostSource, invokeRunner);
        }

        return await RunColdAsync(solution, hostSource, invokeRunner);
    }

    // The source a run that would open its own workspace uses: the host's when one was supplied, a fresh
    // one-shot otherwise. One helper so every cold branch — including the capture-replay fallback and
    // the notice wrapper's inner — resolves it the same way.
    private static ISolutionSource SourceOrCold(ISolutionSource? hostSource)
    {
        return hostSource ?? new ColdSolutionSource();
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
        ReplayedSolution replayed = ReplayOrThrow(binlogFullPath, solutionPath, binlog, diagnostics, ct);
        try
        {
            if (!request.NoCache)
            {
                string? cacheRoot = environment.CacheRootOverride();
                new BinlogCaptureStore(solutionPath, cacheRoot).Ingest(replayed.Solution, binlogFullPath, binlog, ct);
            }

            var source = new ReplayedSolutionSource(
                replayed.Solution, replayed.LoadDiagnosticsWith(diagnostics), replayed.TargetFrameworks);
            return await invokeRunner(solutionPath, source);
        }
        finally
        {
            replayed.Dispose();
        }
    }

    // A structurally-valid capture: replay it lazily, so a fragment-cache hit stays replay-free and
    // byte-identical to a plain cached run. A runtime replay failure of the validated copy is recoverable —
    // acquisition precedes all rendering, so we print the notice and re-run cold (MSBuildLocator is already
    // registered from the up-front call); the two runner invocations build independent runners and the failed
    // one wrote nothing, so the retry cannot double-render.
    private static async Task<int> RunCaptureReplayAsync(
        string binlogCopyPath, string solutionPath, TextWriter error, ISolutionSource? hostSource,
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
            return await invokeRunner(solutionPath, SourceOrCold(hostSource));
        }
    }

    // A stale/unreadable capture: run cold, printing the capture's notice at workspace-acquisition time
    // (never at startup) so a fragment-cache hit acquires nothing and prints nothing.
    private static async Task<int> RunNoticeColdAsync(
        string notice, string solutionPath, TextWriter error, ISolutionSource? hostSource,
        Func<string?, ISolutionSource, Task<int>> invokeRunner)
    {
        Record(GateAcquisition.NoticeCold);
        var source = new NoticingSolutionSource(notice, error, SourceOrCold(hostSource));
        return await invokeRunner(solutionPath, source);
    }

    // No capture and no explicit binlog (or --no-cache): the plain cold path, silent and byte-identical.
    private static async Task<int> RunColdAsync(
        string? solution, ISolutionSource? hostSource, Func<string?, ISolutionSource, Task<int>> invokeRunner)
    {
        Record(GateAcquisition.Cold);
        return await invokeRunner(solution, SourceOrCold(hostSource));
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
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
        IEnvironment? environment, CancellationToken ct)
    {
        return new CheckRunner(output, error, source, environment).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeStatusAsync(
        StatusRequest request, TextWriter output, TextWriter error, ISolutionSource source,
        IEnvironment? environment, CancellationToken ct)
    {
        return new StatusRunner(output, error, source, environment).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeGraphAsync(
        GraphRequest request, TextWriter output, TextWriter error, ISolutionSource source,
        IEnvironment? environment, CancellationToken ct)
    {
        return new GraphRunner(output, error, source, environment).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeExplainAsync(
        ExplainRequest request, TextWriter output, TextWriter error, ISolutionSource source,
        IEnvironment? environment, CancellationToken ct)
    {
        return new ExplainRunner(output, error, source, environment).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeRenderAsync(
        RenderRequest request, TextWriter output, TextWriter error, ISolutionSource source,
        IEnvironment? environment, CancellationToken ct)
    {
        return new RenderRunner(output, error, source, environment).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeBaselineAsync(
        BaselineRequest request, TextWriter output, TextWriter error, ISolutionSource source,
        IEnvironment? environment, CancellationToken ct)
    {
        return new BaselineRunner(output, error, source, environment).RunAsync(request, ct);
    }
}
