using Shouldly;
using SlideRule.Cli.Hooks;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     Pins how a stop reads the files a turn wrote out of a Claude Code transcript: which tool calls count as
///     writes, which prompt's calls belong to the turn, and the tolerance that turns a transcript it cannot read
///     into no answer rather than a failed stop.
/// </summary>
public sealed class TurnWritesTests
{
    private const string Root = "C:/work/repo";

    [Fact]
    public async Task ReadAsync_TheEditTools_NameTheFilesTheyWrote()
    {
        // Arrange
        string transcript = new HookTranscript()
            .Call("Edit", $"{Root}/src/A.cs")
            .Call("Write", $"{Root}/src/B.cs")
            .Call("MultiEdit", $"{Root}/src/C.cs")
            .Call("NotebookEdit", $"{Root}/notes/D.ipynb", key: "notebook_path")
            .SaveTo(TranscriptFile());

        // Act
        IReadOnlyList<string>? written = await TurnWrites.ReadAsync(transcript, "p1", Ct);

        // Assert
        written.ShouldBe([$"{Root}/src/A.cs", $"{Root}/src/B.cs", $"{Root}/src/C.cs", $"{Root}/notes/D.ipynb"]);
    }

    [Fact]
    public async Task ReadAsync_ToolsThatWriteNothing_AreNotWrites()
    {
        // Arrange: a read and a search name paths too, and neither changed a byte.
        string transcript = new HookTranscript()
            .Call("Read", $"{Root}/src/A.cs")
            .Call("Grep", $"{Root}/src", key: "path")
            .SaveTo(TranscriptFile());

        // Act
        IReadOnlyList<string>? written = await TurnWrites.ReadAsync(transcript, "p1", Ct);

        // Assert
        written.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadAsync_AnotherPromptsCalls_AreNotTheTurns()
    {
        // Arrange
        string transcript = new HookTranscript()
            .Wrote($"{Root}/src/Earlier.cs", promptId: "p0")
            .Wrote($"{Root}/src/Now.cs", promptId: "p1")
            .SaveTo(TranscriptFile());

        // Act
        IReadOnlyList<string>? written = await TurnWrites.ReadAsync(transcript, "p1", Ct);

        // Assert: an earlier turn's write was checked at that turn's stop.
        written.ShouldBe([$"{Root}/src/Now.cs"]);
    }

    [Fact]
    public async Task ReadAsync_WithNoPrompt_ReadsTheWholeRun()
    {
        // Arrange: a subagent's own transcript is its run, whatever prompt its rows carry.
        string transcript = new HookTranscript()
            .Wrote($"{Root}/src/First.cs", promptId: "p0")
            .Wrote($"{Root}/src/Second.cs", promptId: "p1")
            .SaveTo(TranscriptFile());

        // Act
        IReadOnlyList<string>? written = await TurnWrites.ReadAsync(transcript, promptId: null, Ct);

        // Assert
        written.ShouldBe([$"{Root}/src/First.cs", $"{Root}/src/Second.cs"]);
    }

    [Fact]
    public async Task ReadAsync_BackslashedPaths_ComeBackForwardSlashedAndOnce()
    {
        // Arrange: the client records a path as the tool was given it, and Windows paths arrive both ways.
        string transcript = new HookTranscript()
            .Wrote(@"C:\work\repo\src\A.cs")
            .Call("Edit", "C:/work/repo/src/A.cs")
            .Call("Edit", "src/Relative.cs")
            .SaveTo(TranscriptFile());

        // Act
        IReadOnlyList<string>? written = await TurnWrites.ReadAsync(transcript, "p1", Ct);

        // Assert: one file, once; a relative path names no tree and is dropped.
        written.ShouldBe([$"{Root}/src/A.cs"]);
    }

    [Fact]
    public async Task ReadAsync_LinesThatDoNotParse_ArePassedOver()
    {
        // Arrange: the client is still appending, so the last line can be cut short.
        string transcript = new HookTranscript()
            .Raw("""{"type":"assistant","message":{"content":[{"type":"tool_use",""")
            .Raw("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t9","name":"Write","input":"x"}]}}""")
            .Wrote($"{Root}/src/A.cs")
            .Raw("""{"type":"user","promptId":"p1","message":{"content":[{"type":"tool_result","tool""")
            .SaveTo(TranscriptFile());

        // Act
        IReadOnlyList<string>? written = await TurnWrites.ReadAsync(transcript, "p1", Ct);

        // Assert
        written.ShouldBe([$"{Root}/src/A.cs"]);
    }

    [Fact]
    public async Task ReadAsync_NoTranscript_IsNoAnswer()
    {
        // Act
        IReadOnlyList<string>? written = await TurnWrites.ReadAsync(TranscriptFile(), "p1", Ct);

        // Assert: null, which the stop reads as doubt, rather than an empty turn or a throw.
        written.ShouldBeNull();
    }

    private static string TranscriptFile()
    {
        return Path.Combine(TestTempRoot.For("turn-writes"), $"{Guid.NewGuid():N}.jsonl");
    }
}
