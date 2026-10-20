using System.Diagnostics;
using Serilog;

namespace SlideRule.Cli.Mcp.Infrastructure;

/// <summary>
///     Defensive idle-timeout shutdown, complementing <see cref="ParentProcessWatcher" />.
///     Tracks time since the last tool call and exits after
///     SLIDERULE_IDLE_TIMEOUT_MINUTES of inactivity.
/// </summary>
/// <remarks>
///     When an MCP client drops/recreates a connection abnormally without closing this server's stdin pipe and the
///     watched parent PID stays alive, the SDK's stdin-EOF detection is missed and the server orphans; this is the
///     backstop for that case. Timed on Stopwatch, which on Windows reads QueryPerformanceCounter and so counts
///     time the machine spends asleep, and on Linux and macOS reads a monotonic clock that stops while the machine
///     is suspended. On Windows a suspend longer than the timeout therefore ends the server at the first poll after
///     wake, the same exit an idle server makes; elsewhere the idle time resumes where the suspend stopped it. A
///     call in flight never counts as idle; a server that never receives a call still exits, timed from process
///     start.
/// </remarks>
internal static class IdleTimeoutWatchdog
{
    internal const int DefaultTimeoutMinutes = 30;

    private static readonly Lock DrainLock = new();
    private static long s_lastActivityTicks;
    private static int s_inFlightCount;
    private static TaskCompletionSource? s_drainWaiter;
    private static Func<long> s_timestampProvider = Stopwatch.GetTimestamp;

    /// <summary>
    ///     The number of tool calls executing. Calls parked on an outer filter have not yet reached
    ///     <see cref="EnterCall" />, so they are not counted.
    /// </summary>
    private static int InFlightCount => Volatile.Read(ref s_inFlightCount);

    /// <summary>Records the start of a tool call; pair it with <see cref="ExitCall" /> in a <c>finally</c>.</summary>
    public static void EnterCall()
    {
        Interlocked.Exchange(ref s_lastActivityTicks, s_timestampProvider());
        Interlocked.Increment(ref s_inFlightCount);
    }

    /// <summary>
    ///     Records the completion of a tool call, releasing a drain parked in <see cref="WhenAllCallsComplete" />
    ///     when it was the last in flight.
    /// </summary>
    /// <remarks>
    ///     Idle is measured from completion, so the timeout window is the gap between responses, not the time since a
    ///     call began.
    /// </remarks>
    public static void ExitCall()
    {
        Interlocked.Exchange(ref s_lastActivityTicks, s_timestampProvider());
        int remaining = Interlocked.Decrement(ref s_inFlightCount);
        if (remaining > 0) return;

        // The drain's count-check + waiter-registration and this signal both run under DrainLock, and the
        // decision to signal uses the exact post-decrement count — so there is no lost-wakeup window.
        lock (DrainLock)
        {
            s_drainWaiter?.TrySetResult();
            s_drainWaiter = null;
        }
    }

    /// <summary>A task that completes when no tool call is in flight.</summary>
    /// <remarks>
    ///     The count is checked and the waiter registered under the lock <see cref="ExitCall" /> signals under, and
    ///     the waiter runs its continuations asynchronously so the signalling thread never runs one while holding
    ///     the lock.
    /// </remarks>
    internal static Task WhenAllCallsComplete()
    {
        lock (DrainLock)
        {
            if (InFlightCount <= 0) return Task.CompletedTask;
            s_drainWaiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return s_drainWaiter.Task;
        }
    }

    /// <summary>
    ///     Attaches a background watcher that exits the process after <paramref name="rawTimeoutMinutes" />, the
    ///     unparsed <c>SLIDERULE_IDLE_TIMEOUT_MINUTES</c> value, with no tool activity. Returns silently when the
    ///     timeout resolves to zero.
    /// </summary>
    public static void Start(string? rawTimeoutMinutes)
    {
        // A 30-second poll against a timeout measured in minutes: the cost of overshooting the deadline by
        // up to one interval is a leaked idle process for half a minute, so the wake-ups stay cheap.
        Start(
            ParseTimeoutMinutes(rawTimeoutMinutes),
            TimeSpan.FromSeconds(30),
            Stopwatch.GetTimestamp,
            () => ServerShutdown.ExitWith("idle timeout"));
    }

    internal static void Start(
        TimeSpan timeout, TimeSpan pollInterval, Func<long> clock, Action onIdleTimeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            Log.Information("Idle-timeout watchdog disabled (SLIDERULE_IDLE_TIMEOUT_MINUTES=0)");
            return;
        }

        s_timestampProvider = clock;
        Interlocked.Exchange(ref s_lastActivityTicks, clock());
        // Warning so a post-mortem at default min-level confirms attach (cf. ParentProcessWatcher).
        Log.Warning("Idle-timeout watchdog attached: {Minutes} min timeout", timeout.TotalMinutes);
        // Detached, never awaited: the poll loop runs for the life of the server, and Start must return so
        // the transport can begin serving.
        _ = Task.Run(() => WatchAsync(timeout, pollInterval, clock, onIdleTimeout));
    }

    internal static bool IsIdleExpired(TimeSpan timeout, long nowTicks)
    {
        if (Volatile.Read(ref s_inFlightCount) > 0) return false;

        long last = Interlocked.Read(ref s_lastActivityTicks);
        return Stopwatch.GetElapsedTime(last, nowTicks) > timeout;
    }

    private static async Task WatchAsync(
        TimeSpan timeout, TimeSpan pollInterval, Func<long> clock, Action onIdleTimeout)
    {
        try
        {
            using var timer = new PeriodicTimer(pollInterval);
            while (await timer.WaitForNextTickAsync())
                if (IsIdleExpired(timeout, clock()))
                {
                    Log.Warning("No tool activity for over {Minutes} min; shutting down idle server",
                        timeout.TotalMinutes);
                    onIdleTimeout();
                    return;
                }
        }
        catch (Exception ex)
        {
            // Keep serving, unlike the parent-process watcher, which exits when it fails: the server still ends
            // when its client exits (through that watcher on Windows, stdin EOF elsewhere), so a failed idle
            // loop costs at most an idle process, while exiting here would end the session whether or not it
            // is in use.
            Log.Warning(ex, "Idle-timeout watchdog loop failed; watchdog disabled");
        }
    }

    /// <summary>Parses a raw <c>SLIDERULE_IDLE_TIMEOUT_MINUTES</c> value into an idle timeout.</summary>
    /// <remarks>
    ///     null/blank/invalid/negative => the default, so the watchdog stays on: a typo must
    ///     not silently disable leak protection. <c>"0"</c> => <see cref="TimeSpan.Zero" />, the
    ///     explicit, documented opt-out.
    /// </remarks>
    internal static TimeSpan ParseTimeoutMinutes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return TimeSpan.FromMinutes(DefaultTimeoutMinutes);

        if (int.TryParse(raw, out int minutes) && minutes >= 0) return TimeSpan.FromMinutes(minutes);

        Log.Warning("Invalid SLIDERULE_IDLE_TIMEOUT_MINUTES '{Raw}'; using {Default} min default",
            raw, DefaultTimeoutMinutes);
        return TimeSpan.FromMinutes(DefaultTimeoutMinutes);
    }

    internal static void ResetForTests()
    {
        Interlocked.Exchange(ref s_inFlightCount, 0);
        Interlocked.Exchange(ref s_lastActivityTicks, 0);
        s_timestampProvider = Stopwatch.GetTimestamp;
        lock (DrainLock)
        {
            s_drainWaiter = null;
        }
    }
}
