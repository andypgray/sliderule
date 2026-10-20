using Serilog;

namespace SlideRule.Cli.Mcp.Infrastructure;

/// <summary>
///     The watchdogs' exit path: waits for an in-flight tool call to finish, runs the registered async disposers
///     with a bound each, and flushes Serilog before terminating the process.
/// </summary>
/// <remarks>
///     A registered disposer may own out-of-process state, such as a long-lived
///     <see cref="Roslyn.WorkspaceSession" />'s <c>MSBuildWorkspace</c> and its BuildHost child, which a bare
///     process exit would strand. A per-disposer bound keeps shutdown deterministic even if a disposer hangs.
/// </remarks>
internal static class ServerShutdown
{
    // 12s bound per disposer: sized to a long-lived MSBuild server's measured worst case, where a
    // workspace dispose (acquire the session's mutation gate, then dispose the MSBuildWorkspace + BuildHost
    // child) takes ~10s. The warm WorkspaceSession's own DisposeAsync uses the same 12s gate-acquire bound,
    // so a 5s bound here could abandon its dispose partway.
    private static readonly TimeSpan DefaultDisposalTimeout = TimeSpan.FromSeconds(12);

    // How long ExitWith waits for in-flight tool calls to finish before running disposers. Bounds the
    // window that lets a mid-flight call commit; 5s keeps shutdown from being held hostage by a stuck call.
    private static readonly TimeSpan DefaultInFlightDrainTimeout = TimeSpan.FromSeconds(5);
    private static readonly Lock DisposersLock = new();
    private static readonly List<Func<ValueTask>> Disposers = [];
    private static int s_hasExited;

    /// <summary>
    ///     True once <see cref="ExitWith(string)" /> has been accepted by the gate. Subsequent
    ///     callers return silently — disposal runs at most once, no matter how many watchdogs
    ///     race to exit.
    /// </summary>
    internal static bool HasExited => Volatile.Read(ref s_hasExited) != 0;

    /// <summary>
    ///     Adds <paramref name="dispose" /> to the disposers <see cref="ExitWith(string)" /> runs, in
    ///     registration order.
    /// </summary>
    public static void RegisterDisposer(Func<ValueTask> dispose)
    {
        ArgumentNullException.ThrowIfNull(dispose);
        lock (DisposersLock)
        {
            Disposers.Add(dispose);
        }
    }

    /// <summary>
    ///     Shuts the server down and exits with code 0, logging <paramref name="reason" />, a short label such as
    ///     <c>"idle timeout"</c>, at Warning so a post-mortem can identify the trigger.
    /// </summary>
    public static void ExitWith(string reason)
    {
        ExitWith(reason, static () => Environment.Exit(0), DefaultDisposalTimeout, DefaultInFlightDrainTimeout);
    }

    internal static void ExitWith(string reason, Action exit, TimeSpan disposalTimeout, TimeSpan? inFlightDrainTimeout = null)
    {
        if (Interlocked.CompareExchange(ref s_hasExited, 1, 0) != 0) return;

        Log.Warning("Shutting down: {Reason}", reason);

        WaitForInFlightCalls(inFlightDrainTimeout ?? DefaultInFlightDrainTimeout);

        List<Func<ValueTask>> snapshot;
        lock (DisposersLock)
        {
            snapshot = [.. Disposers];
        }

        foreach (Func<ValueTask> disposer in snapshot) RunDisposerBounded(disposer, disposalTimeout);

        Log.CloseAndFlush();
        exit();
    }

    /// <summary>
    ///     Blocks until no tool call is in flight or <paramref name="timeout" /> elapses: this is the terminal
    ///     shutdown path, so blocking the caller is fine.
    /// </summary>
    private static void WaitForInFlightCalls(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return;

        Task drained = IdleTimeoutWatchdog.WhenAllCallsComplete();
        drained.Wait(timeout);
    }

    private static void RunDisposerBounded(Func<ValueTask> disposer, TimeSpan timeout)
    {
        try
        {
            Task disposeTask = disposer().AsTask();
            if (!disposeTask.Wait(timeout)) Log.Warning("Disposer timed out after {Timeout}; continuing exit", timeout);
        }
        catch (AggregateException ae)
        {
            // Task.Wait wraps disposer faults; unwrap so the log shows the real exception.
            Log.Warning(ae.GetBaseException(), "Disposer faulted; continuing exit");
        }
        catch (Exception ex)
        {
            // Synchronous throw from the disposer itself (before it returned a Task).
            Log.Warning(ex, "Disposer threw synchronously; continuing exit");
        }
    }

    internal static void ResetForTests()
    {
        lock (DisposersLock)
        {
            Disposers.Clear();
        }

        Interlocked.Exchange(ref s_hasExited, 0);
    }
}
