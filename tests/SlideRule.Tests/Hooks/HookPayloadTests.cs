using Shouldly;
using SlideRule.Cli.Hooks;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     Pins how <c>sliderule hook</c> reads its payload: the fields it takes from a Claude Code event, and the
///     tolerance that makes a payload which will not parse a hand run rather than a crash.
/// </summary>
public sealed class HookPayloadTests
{
    [Fact]
    public void Parse_StopPayload_ReadsEveryFieldTheHookUses()
    {
        // Arrange
        const string text = """
                            {"hook_event_name":"SubagentStop","session_id":"s1","prompt_id":"p1","agent_id":"a1",
                             "stop_hook_active":true,"cwd":"C:\\work\\repo","transcript_path":"C:\\t\\s.jsonl",
                             "agent_transcript_path":"C:\\t\\s\\agent-a1.jsonl"}
                            """;

        // Act
        HookPayload payload = HookPayload.Parse(text);

        // Assert: every path comes back forward-slashed, the one spelling everything downstream compares.
        payload.ShouldBe(new HookPayload(
            "SubagentStop", "s1", "p1", "a1", "C:/work/repo", null, true, "C:/t/s.jsonl", "C:/t/s/agent-a1.jsonl"));
    }

    [Fact]
    public void Parse_EditPayload_ReadsTheEditedFileOutOfTheToolInput()
    {
        // Act
        HookPayload payload = HookPayload.Parse("""{"hook_event_name":"PostToolUse","tool_input":{"file_path":"src\\A.cs"}}""");

        // Assert
        payload.EditedFile.ShouldBe("src/A.cs");
        payload.StopHookActive.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json {")]
    [InlineData("[1, 2]")]
    [InlineData("\"a string\"")]
    public void Parse_TextThatIsNotAnObject_IsNoPayload(string text)
    {
        // Act
        HookPayload payload = HookPayload.Parse(text);

        // Assert: a hand run, never a crash.
        payload.ShouldBe(HookPayload.None);
    }

    [Fact]
    public void Parse_FieldsOfAnotherKindOrEmpty_ReadAsAbsent()
    {
        // Act
        HookPayload payload = HookPayload.Parse(
            """{"hook_event_name":42,"session_id":"","cwd":null,"tool_input":"src/A.cs","stop_hook_active":"true"}""");

        // Assert: only a JSON true stands down, and only a non-empty string names anything.
        payload.ShouldBe(HookPayload.None);
    }
}
