using System.Text.Json.Nodes;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     The Claude Code event payloads the hook tests put on stdin, serialized rather than spliced into a literal, so
///     a path is escaped the way JSON escapes it and a key a payload does not carry stays absent.
/// </summary>
internal static class HookPayloads
{
    /// <summary>The session every payload names unless a row needs a second one.</summary>
    internal const string Session = "s1";

    /// <summary>
    ///     A turn-end payload: the fields the hook reads out of a <c>Stop</c> or <c>SubagentStop</c> document, for a
    ///     session that ran from <paramref name="cwd" />. <paramref name="agentId" /> is carried only on a worker's
    ///     stop, as Claude Code sends it: the main agent's payload has no such key. The two transcripts are carried
    ///     only when a row names them.
    /// </summary>
    internal static string Stop(
        string cwd,
        string sessionId = Session,
        string promptId = "p1",
        bool stopHookActive = false,
        string eventName = "Stop",
        string? agentId = null,
        string? transcriptPath = null,
        string? agentTranscriptPath = null)
    {
        var payload = new JsonObject
        {
            ["hook_event_name"] = eventName,
            ["session_id"] = sessionId,
            ["prompt_id"] = promptId,
            ["stop_hook_active"] = stopHookActive
        };
        if (agentId is not null) payload["agent_id"] = agentId;
        if (transcriptPath is not null) payload["transcript_path"] = transcriptPath;
        if (agentTranscriptPath is not null) payload["agent_transcript_path"] = agentTranscriptPath;
        payload["cwd"] = cwd;

        return payload.ToJsonString();
    }

    /// <summary>
    ///     A <c>UserPromptSubmit</c> payload: the fields the hook reads when a prompt arrives, for the session in
    ///     <see cref="Session" />, which ran from <paramref name="cwd" />.
    /// </summary>
    internal static string Prompt(string cwd)
    {
        var payload = new JsonObject
        {
            ["hook_event_name"] = "UserPromptSubmit",
            ["session_id"] = Session,
            ["prompt_id"] = "p1",
            ["cwd"] = cwd,
            ["prompt"] = "what changed?"
        };

        return payload.ToJsonString();
    }

    /// <summary>
    ///     A PostToolUse payload naming <paramref name="path" />, escaped the way JSON escapes it, which on Windows
    ///     doubles every separator: the spelling the hook has to undo before it can compare the path to anything.
    /// </summary>
    internal static string Edit(string path)
    {
        var payload = new JsonObject
        {
            ["tool_input"] = new JsonObject { ["file_path"] = path }
        };

        return payload.ToJsonString();
    }
}
