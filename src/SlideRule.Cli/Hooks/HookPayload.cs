using System.Text.Json;

namespace SlideRule.Cli.Hooks;

/// <summary>
///     The fields of a Claude Code hook payload that <c>sliderule hook</c> reads: which event fired, whose
///     session and prompt it belongs to, where the session is, and what an edit touched.
/// </summary>
/// <remarks>
///     Parsed tolerantly, because a payload that will not parse is a hand run rather than an error. Text that is
///     not a JSON object is no payload at all, and a field that is absent, empty or of another kind reads as
///     absent. The two paths come back with their separators turned forward, so everything that compares them
///     compares one spelling.
/// </remarks>
/// <param name="EventName">The <c>hook_event_name</c>, or null.</param>
/// <param name="SessionId">The <c>session_id</c>, or null.</param>
/// <param name="PromptId">The <c>prompt_id</c>, or null.</param>
/// <param name="AgentId">The <c>agent_id</c> a subagent's event carries, or null for the main agent.</param>
/// <param name="Cwd">The session's directory (<c>cwd</c>), forward-slashed, or null.</param>
/// <param name="EditedFile">The edited file (<c>tool_input.file_path</c>), forward-slashed, or null.</param>
/// <param name="StopHookActive">Whether <c>stop_hook_active</c> is <c>true</c>.</param>
internal sealed record HookPayload(
    string? EventName,
    string? SessionId,
    string? PromptId,
    string? AgentId,
    string? Cwd,
    string? EditedFile,
    bool StopHookActive)
{
    /// <summary>No payload: what a hand run at a terminal, and text that is not a JSON object, both read as.</summary>
    public static HookPayload None { get; } = new(null, null, null, null, null, null, false);

    /// <summary>Reads the payload out of <paramref name="text" />, the whole of what arrived on stdin.</summary>
    /// <param name="text">The text read from stdin, or null when nothing was read.</param>
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
                stopHookActive);
        }
        catch (JsonException)
        {
            return None;
        }
    }

    private static string? StringOf(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return null;

        string? text = value.GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? Forward(string? path)
    {
        return path?.Replace('\\', '/');
    }
}
