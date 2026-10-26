using System.Text.Json;
using SlideRule.Internal;

namespace SlideRule.Cli.Hooks;

/// <summary>
///     The files a turn, or a subagent's run, wrote through the agent's edit tools, as the transcript Claude Code
///     keeps for it records them: the way a stop learns which of the project's trees an agent wrote into by an
///     absolute path while its directory stayed elsewhere.
/// </summary>
/// <remarks>
///     The transcript is the client's own format, one JSON object per line, read as it is still being appended
///     to. An assistant row carries the tool calls, and the user row answering one carries its result under
///     the prompt's id, so a turn's writes are the calls whose results that prompt's rows hold
///     (verified 2026-10-20 on Claude Code 2.1.300). A line that will not parse, such as the last one half
///     written, is passed over. The client appends rows from a queue it drains on a 100 ms timer and does not
///     drain before a stop's hooks run, so a write is on disk at the stop because a model response always follows
///     its result, and a writer that stalls for longer than that response hides the write
///     (measured 2026-10-20 on Claude Code 2.1.300). Writes made through a shell never appear here either, which
///     is why the session's own tree is always checked as well.
/// </remarks>
internal static class TurnWrites
{
    private static readonly string[] FilePathTools = ["Edit", "Write", "MultiEdit"];
    private const string NotebookTool = "NotebookEdit";

    /// <summary>
    ///     The absolute, forward-slashed paths the edit tools wrote, in first-written order: under prompt
    ///     <paramref name="promptId" />, or for a null id across the whole transcript, which is how a subagent's
    ///     own transcript holds its run. Null when the transcript cannot be read.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> ReadAsync(
        string transcript, string? promptId, CancellationToken ct)
    {
        var written = new List<(string Id, string Path)>();
        var answered = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            await using var stream = new FileStream(
                transcript, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                // Text a row must hold to matter, tested before the row is parsed: the parse still decides.
                if (line.Contains("\"tool_use\"", StringComparison.Ordinal) && NamesAWrittenPath(line))
                    written.AddRange(ToolWrites(line));
                if (promptId is not null
                    && line.Contains("\"tool_result\"", StringComparison.Ordinal)
                    && line.Contains(promptId, StringComparison.Ordinal))
                    answered.UnionWith(ResultsUnder(line, promptId));
            }
        }
        catch (Exception ex) when (HookState.IsFileSystemFailure(ex))
        {
            return null;
        }

        return written.Where(write => promptId is null || answered.Contains(write.Id))
            .Select(static write => write.Path)
            .Distinct(PathComparison.Comparer)
            .ToList();
    }

    // Whether a line holds a property PathOf reads.
    private static bool NamesAWrittenPath(string line)
    {
        return line.Contains("\"file_path\"", StringComparison.Ordinal)
               || line.Contains("\"notebook_path\"", StringComparison.Ordinal);
    }

    // The writes an assistant row's tool calls name: the id of each call with the absolute path it wrote.
    private static IEnumerable<(string Id, string Path)> ToolWrites(string line)
    {
        if (Row(line, "assistant") is not { } content) return [];

        var writes = new List<(string, string)>();
        foreach (JsonElement block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object
                || HookPayload.StringOf(block, "type") != "tool_use"
                || HookPayload.StringOf(block, "id") is not { } id
                || HookPayload.StringOf(block, "name") is not { } name
                || !block.TryGetProperty("input", out JsonElement input)
                || input.ValueKind != JsonValueKind.Object)
                continue;

            string? path = HookPayload.Forward(PathOf(name, input));
            if (path is not null && HookGit.IsRooted(path)) writes.Add((id, path));
        }

        return writes;
    }

    // The tool calls a user row answers, when the row belongs to prompt `promptId`.
    private static IEnumerable<string> ResultsUnder(string line, string promptId)
    {
        if (Row(line, "user", promptId) is not { } content) return [];

        return content.EnumerateArray()
            .Where(static block => block.ValueKind == JsonValueKind.Object
                                   && HookPayload.StringOf(block, "type") == "tool_result")
            .Select(static block => HookPayload.StringOf(block, "tool_use_id"))
            .OfType<string>()
            .ToList();
    }

    // The content array of a row of the given type, and under the given prompt when one is named, or null for any
    // other row and for a line that does not parse. The array is cloned, so it outlives the parsed document.
    private static JsonElement? Row(string line, string type, string? promptId = null)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || HookPayload.StringOf(root, "type") != type
                || (promptId is not null && HookPayload.StringOf(root, "promptId") != promptId)
                || !root.TryGetProperty("message", out JsonElement message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out JsonElement content)
                || content.ValueKind != JsonValueKind.Array)
                return null;

            return content.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The path a call to tool `name` writes, read from its input, or null for a tool that writes nothing.
    private static string? PathOf(string name, JsonElement input)
    {
        if (FilePathTools.Contains(name, StringComparer.Ordinal)) return HookPayload.StringOf(input, "file_path");

        return name == NotebookTool ? HookPayload.StringOf(input, "notebook_path") : null;
    }
}
