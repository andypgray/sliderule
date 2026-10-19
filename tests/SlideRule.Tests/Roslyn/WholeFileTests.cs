using System.Text;
using Shouldly;
using SlideRule.Rendering;
using SlideRule.Roslyn.Files;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The whole-file writer's four promises: it creates the file and the directory above it; identical
///     content is a true no-op, timestamp included; a checkout that rewrote the line endings is identical
///     content and is left alone; and a file that must change lands LF with no BOM.
/// </summary>
/// <remarks>
///     The timestamp claim is the one worth stating separately from the outcome word. Every consumer of a
///     generated file that a build regenerates on every run — an incremental build, a file watcher, a
///     reviewer reading a diff — keys on the write time rather than on what this returns, so an
///     <c>Unchanged</c> that still touched the file would be a lie told to all three.
/// </remarks>
public sealed class WholeFileTests
{
    private const string Body = "{\n  \"one\": 1\n}\n";

    // An instant no test run can produce by accident, so a rewritten file is unmistakable rather than
    // merely improbable.
    private static readonly DateTime Stamped = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    [Fact]
    public void Write_NoFileAndNoDirectory_CreatesBothAndReportsWrote()
    {
        using TempDirectory temp = TestTempRoot.Fresh("whole-file");
        string path = temp.PathOf("nested", "deeper", "model.json");

        WriteOutcome outcome = WholeFile.Write(path, Body);

        outcome.ShouldBe(WriteOutcome.Wrote);
        File.ReadAllText(path)
            .ShouldBe(Body);
    }

    [Fact]
    public void Write_TheSameContentAgain_IsUnchangedAndDoesNotTouchTheFile()
    {
        using TempDirectory temp = TestTempRoot.Fresh("whole-file");
        string path = temp.WriteFile(["model.json"], Body);
        File.SetLastWriteTimeUtc(path, Stamped);

        WriteOutcome outcome = WholeFile.Write(path, Body);

        outcome.ShouldBe(WriteOutcome.Unchanged);
        File.GetLastWriteTimeUtc(path)
            .ShouldBe(Stamped);
    }

    [Fact]
    public void Write_ACrlfCopyOfTheSameContent_IsUnchangedAndStaysCrlf()
    {
        // The autocrlf checkout. The bytes on disk are not the bytes that would be written, and rewriting
        // them would put every such checkout permanently one diff away from clean — so the compare
        // normalizes and the file keeps its own endings.
        using TempDirectory temp = TestTempRoot.Fresh("whole-file");
        string path = temp.WriteFile(["model.json"], Body.Replace("\n", "\r\n"));
        File.SetLastWriteTimeUtc(path, Stamped);

        WriteOutcome outcome = WholeFile.Write(path, Body);

        outcome.ShouldBe(WriteOutcome.Unchanged);
        File.ReadAllText(path)
            .ShouldContain("\r\n");
        File.GetLastWriteTimeUtc(path)
            .ShouldBe(Stamped);
    }

    [Fact]
    public void Write_ChangedContent_LandsLfWithNoBom()
    {
        using TempDirectory temp = TestTempRoot.Fresh("whole-file");
        string path = temp.WriteFile(["model.json"], "{\n  \"one\": 0\n}\n");

        WriteOutcome outcome = WholeFile.Write(path, Body);

        outcome.ShouldBe(WriteOutcome.Wrote);
        byte[] bytes = File.ReadAllBytes(path);
        // A UTF-8 preamble is three bytes, so the first three are what says whether one was emitted.
        byte[] leading = bytes.Take(3)
            .ToArray();
        leading.ShouldNotBe(Encoding.UTF8.GetPreamble());
        // And the decoded text says the rest: a preamble would survive as U+FEFF and a CRLF as a stray \r,
        // so neither can hide behind a decode.
        Encoding.UTF8.GetString(bytes)
            .ShouldBe(Body);
    }
}
