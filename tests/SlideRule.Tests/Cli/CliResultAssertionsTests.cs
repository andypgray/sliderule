using Shouldly;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     <see cref="CliResultAssertions.ShouldOpenWith" /> and <see cref="CliResultAssertions.ShouldPrintVerbatim" />
///     fold the CRLF the in-process capture writes on Windows and nothing else: each passes over CRLF output, and
///     reds with the run's transcript on output that differs in any other way.
/// </summary>
public sealed class CliResultAssertionsTests
{
    [Fact]
    public void ShouldOpenWith_CrlfStdoutOpeningWithTheText_Passes()
    {
        var result = new CliResult(0, "stamp\r\n\r\nverdict\r\n", "");

        result.ShouldOpenWith("stamp\n\n");
    }

    [Fact]
    public void ShouldOpenWith_StdoutWithoutTheBlankLine_RedsWithTheTranscript()
    {
        var result = new CliResult(0, "stamp\r\nverdict\r\n", "");

        var red = Should.Throw<ShouldAssertException>(() => result.ShouldOpenWith("stamp\n\n"));

        red.Message.ShouldContain("The CLI exited 0.");
    }

    [Fact]
    public void ShouldPrintVerbatim_CrlfStdout_Passes()
    {
        var result = new CliResult(0, "line one\r\nline two\r\n", "");

        result.ShouldPrintVerbatim("line one\nline two\n");
    }

    [Fact]
    public void ShouldPrintVerbatim_StdoutWithoutItsTrailingNewline_RedsWithTheTranscript()
    {
        var result = new CliResult(0, "line one\r\nline two", "");

        var red = Should.Throw<ShouldAssertException>(() => result.ShouldPrintVerbatim("line one\nline two\n"));

        red.Message.ShouldContain("The CLI exited 0.");
    }
}
