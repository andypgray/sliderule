using System.Text.Json;

namespace SlideRule.Cli.Hooks;

/// <summary>The fields of a Claude Code hook payload that <c>sliderule hook</c> reads.</summary>
/// <remarks>
///     Parsed tolerantly, because a payload that will not parse is a hand run rather than an error. Text that is
///     not a JSON object is no payload at all, and a field that is absent, empty or of another kind reads as
///     absent: null, or false for <paramref name="StopHookActive" />. The paths come back with their separators
///     turned forward, so everything that compares them compares one spelling. <paramref name="AgentId" /> is
///     null for the main agent, and <paramref name="AgentTranscriptPath" /> is the transcript of the subagent a
///     <c>SubagentStop</c> is for (verified 2026-10-20 on Claude Code 2.1.300).
/// </remarks>
internal sealed record HookPayload(
    string? EventName,
    string? SessionId,
    string? PromptId,
    string? AgentId,
    string? Cwd,
    string? EditedFile,
    bool StopHookActive,
    string? TranscriptPath = null,
    string? AgentTranscriptPath = null)
{
    /// <summary>No payload: what a hand run at a terminal, and text that is not a JSON object, both read as.</summary>
    public static HookPayload None { get; } = new(null, null, null, null, null, null, false);

    /// <summary>Reads the payload out of <paramref name="text" />, the whole of what arrived on stdin.</summary>
    public static HookPayload Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return None;

            string? editedFile = root.TryGetProperty("tool_input", out JsonElement toolInput)
                                 && toolInput.ValueKind == JsonValueKind.Object
                ? StringOf(toolInput, "file_path")
                : null;
            bool stopHookActive = root.TryGetProperty("stop_hook_active", out JsonElement active)
                                  && active.ValueKind == JsonValueKind.True;

            return new HookPayload(
                StringOf(root, "hook_event_name"),
                StringOf(root, "session_id"),
                StringOf(root, "prompt_id"),
                StringOf(root, "agent_id"),
                Forward(StringOf(root, "cwd")),
                Forward(editedFile),
                stopHookActive,
                Forward(StringOf(root, "transcript_path")),
                Forward(StringOf(root, "agent_transcript_path")));
        }
        catch (JsonException)
        {
            return None;
        }
    }

    /// <summary>
    ///     The string property <paramref name="name" /> of <paramref name="element" />, or null when it is absent,
    ///     empty or of another kind.
    /// </summary>
    internal static string? StringOf(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return null;

        string? text = value.GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary><paramref name="path" /> with its separators turned forward.</summary>
    internal static string? Forward(string? path)
    {
        return path?.Replace('\\', '/');
    }
}
