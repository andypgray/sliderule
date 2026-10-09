using System.Text.Json;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Renders a clean check run's report as the JSON a Claude Code hook injects into the agent's
///     transcript — the fourth render target over the same run, and the only one whose reader is the agent
///     rather than a human, a code-scanning service, or a client parsing
///     <see cref="JsonReportRenderer" />'s document.
/// </summary>
/// <remarks>
///     <para>
///         The channel exists because a hook that exits 0 cannot block and its plain stdout reaches nobody,
///         while <c>hookSpecificOutput.additionalContext</c> on that same exit-0 stdout arrives in the
///         transcript as a system message. That is exactly a warning's shape: a tripwire warning has no
///         verdict to change and everything to say.
///     </para>
///     <para>
///         Which event the envelope names is the caller's, because the field is not decoration: Claude Code
///         reads <c>additionalContext</c> only from a document whose <c>hookEventName</c> is the event the
///         hook was fired for, so a hook wired to <c>Stop</c> and handed a <c>PostToolUse</c> envelope
///         gets nothing. <see cref="Events" /> is the whole set that carries context to the agent.
///     </para>
///     <para>
///         The whole job is escaping a multi-line report into a JSON string. Serialization is the shared
///         <see cref="SlideRuleJson.Options" />, so this document and every other document
///         <see cref="SlideRuleJsonContext" /> serializes cannot drift in escaping.
///     </para>
///     <para>
///         Two records rather than a <c>*Dtos.cs</c> twin: this document is one object with one nested
///         object, and its whole wire shape fits beside the renderer that writes it.
///     </para>
/// </remarks>
internal static class HookReportRenderer
{
    /// <summary>The event a run renders for when the request names none: the per-edit shape.</summary>
    public const string DefaultEvent = "PostToolUse";

    /// <summary>The turn-end event the recommended hook fires on: the agent claiming it is done.</summary>
    public const string StopEvent = "Stop";

    /// <summary>The same boundary for a worker: a subagent claiming it is done.</summary>
    public const string SubagentStopEvent = "SubagentStop";

    /// <summary>
    ///     Every event whose additional context reaches the agent, and so the only names this document may
    ///     carry. A hook wired to any other event gets nothing from an envelope, whatever it says.
    /// </summary>
    public static readonly IReadOnlyList<string> Events = [DefaultEvent, StopEvent, SubagentStopEvent];

    /// <summary>
    ///     The one-object document carrying <paramref name="report" /> as additional context for
    ///     <paramref name="hookEvent" />, with no trailing newline (the caller's <c>WriteLine</c> supplies
    ///     it).
    /// </summary>
    public static string Document(string report, string hookEvent)
    {
        var payload = new HookJson(new HookSpecificOutputJson(hookEvent, report));
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
