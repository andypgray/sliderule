using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Hooks.Handoff;

/// <summary>
///     The time one hook firing has, from the client's kill timeout, and the limits the warm hand-off spends it
///     under. Every tree a firing checks spends the one budget.
/// </summary>
/// <param name="Origin">
///     When the client started the hook command, taken as this process's start less the launcher
///     allowance.
/// </param>
/// <param name="ClientTimeout">How long the client lets the hook command run before it kills it.</param>
internal sealed record HookBudget(DateTimeOffset Origin, TimeSpan ClientTimeout)
{
    /// <summary>
    ///     Claude Code's documented command-hook timeout, assumed when <see cref="SlideRuleEnvVars.HookTimeout" />
    ///     names none; <c>hooks/settings.snippet.json</c> sets the same value.
    /// </summary>
    internal static readonly TimeSpan DefaultClientTimeout = TimeSpan.FromSeconds(600);

    /// <summary>
    ///     The longest a launcher that starts this process may spend first, inside the same client timeout; it must
    ///     cover the wait of every launcher a hook command here goes through.
    /// </summary>
    internal static readonly TimeSpan LauncherAllowance = TimeSpan.FromSeconds(60);

    /// <summary>The limit on reaching a server and being admitted, together.</summary>
    internal static readonly TimeSpan DiscoveryLimit = TimeSpan.FromMilliseconds(200);

    /// <summary>The limit on a warm check once the server has admitted it.</summary>
    internal static readonly TimeSpan WarmLimit = TimeSpan.FromSeconds(20);

    /// <summary>What a cold check and the hook's final flush need left, after a warm attempt that fails.</summary>
    internal static readonly TimeSpan ColdReserve = TimeSpan.FromSeconds(120);

    /// <summary>When the client kills the hook.</summary>
    public DateTimeOffset Deadline => Origin + ClientTimeout;

    /// <summary>
    ///     The budget of this process, started by the client, with the timeout <paramref name="environment" />
    ///     names or the default.
    /// </summary>
    public static HookBudget ForThisProcess(IEnvironment environment)
    {
        return new HookBudget(StartedAt() - LauncherAllowance, ClientTimeoutFrom(environment));
    }

    // Now where the platform will not say when the process started, which only shortens the budget. The catch list
    // must agree with PhaseTimings.ProcessAge's, which reads the same start time.
    private static DateTimeOffset StartedAt()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    ///     The client timeout <paramref name="environment" /> names in whole seconds, or the default for a value that
    ///     is missing or not a positive whole number.
    /// </summary>
    internal static TimeSpan ClientTimeoutFrom(IEnvironment environment)
    {
        string? value = environment.NonBlankVariable(SlideRuleEnvVars.HookTimeout);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : DefaultClientTimeout;
    }

    /// <summary>Whether a warm attempt begun at <paramref name="now" /> would still leave the cold reserve.</summary>
    public bool LeavesRoomForWarm(DateTimeOffset now)
    {
        return Deadline - now - DiscoveryLimit - WarmLimit >= ColdReserve;
    }
}
