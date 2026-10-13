using System.Text.Json.Nodes;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     A Claude Code transcript as the hook reads one: JSON lines where an assistant row carries a tool call and the
///     user row after it carries that call's result under the prompt's id, the shape a session's transcript and a
///     subagent's both have. Rows are serialized rather than spliced into a literal, so a path is escaped the way
///     the client escapes it.
/// </summary>
internal sealed class HookTranscript
{
    private readonly List<string> _lines = [];
    private int _calls;

    /// <summary>
    ///     Records a call to <paramref name="tool" /> whose input names <paramref name="path" /> under
    ///     <paramref name="key" />, and its result under prompt <paramref name="promptId" />.
    /// </summary>
    internal HookTranscript Call(string tool, string path, string promptId = "p1", string key = "file_path")
    {
        var id = $"toolu_{++_calls}";
        var call = new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_use",
                    ["id"] = id,
                    ["name"] = tool,
                    ["input"] = new JsonObject { [key] = path }
                })
            }
        };
        var result = new JsonObject
        {
            ["type"] = "user",
            ["promptId"] = promptId,
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = id,
                    ["content"] = "ok"
                })
            }
        };
        _lines.Add(call.ToJsonString());
        _lines.Add(result.ToJsonString());

        return this;
    }

    /// <summary>Records a <c>Write</c> of <paramref name="path" /> under prompt <paramref name="promptId" />.</summary>
    internal HookTranscript Wrote(string path, string promptId = "p1")
    {
        return Call("Write", path, promptId);
    }

    /// <summary>Appends <paramref name="line" /> as it stands: a row the reader has to pass over.</summary>
    internal HookTranscript Raw(string line)
    {
        _lines.Add(line);
        return this;
    }

    /// <summary>Writes the transcript to <paramref name="file" /> and returns its path.</summary>
    internal string SaveTo(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, string.Join("\n", _lines) + "\n");

        return file;
    }
}
