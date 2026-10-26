using System.Text.Json;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Renders a clean check run's report as the JSON a Claude Code hook injects into the agent's transcript.
/// </summary>
/// <remarks>
///     <para>
///         The channel exists because an exit-0 hook's plain stdout goes only to Claude Code's debug log, while
///         <c>hookSpecificOutput.additionalContext</c> on that same stdout reaches the agent as hook feedback,
///         with no error shown. That is a warning's shape: a tripwire warning has no verdict to change and
///         everything to say. On <c>Stop</c> and <c>SubagentStop</c> the feedback also keeps the agent working,
///         so it counts against the same round cap as a block.
///     </para>
///     <para>
///         Which event the envelope names is the caller's, because the field is not decoration: Claude Code
///         rejects, as a hook error, a document whose <c>hookEventName</c> is not the event the hook was fired
///         for, so a hook wired to <c>Stop</c> and handed a <c>PostToolUse</c> envelope gets nothing
///         (verified 2026-10-20 on Claude Code 2.1.300).
///     </para>
/// </remarks>
internal static class HookReportRenderer
{
    /// <summary>The event the per-edit path's document names, a hand run's included.</summary>
    public const string DefaultEvent = "PostToolUse";

    /// <summary>The turn-end event the recommended hook fires on: the agent claiming it is done.</summary>
    public const string StopEvent = "Stop";

    /// <summary>The same boundary for a worker: a subagent claiming it is done.</summary>
    public const string SubagentStopEvent = "SubagentStop";

    /// <summary>
    ///     The one-object document carrying <paramref name="report" /> as additional context for
    ///     <paramref name="hookEvent" />, with no trailing newline (the caller writes it through
    ///     <see cref="SlideRuleJson.Terminated" />). The context's line ends are LF on every OS, whatever the
    ///     report was written with.
    /// </summary>
    public static string Document(string report, string hookEvent)
    {
        var payload = new HookJson(new HookSpecificOutputJson(hookEvent, report.Replace("\r\n", "\n")));
        return JsonSerializer.Serialize(payload, SlideRuleJson.Context.HookJson);
    }
}

/// <summary>The root of the hook document: Claude Code reads one <c>hookSpecificOutput</c> object.</summary>
internal sealed record HookJson(HookSpecificOutputJson HookSpecificOutput);

/// <summary>
///     The hook payload: which event it answers, and the text Claude Code turns into a transcript system
///     message the agent reads.
/// </summary>
internal sealed record HookSpecificOutputJson(string HookEventName, string AdditionalContext);
