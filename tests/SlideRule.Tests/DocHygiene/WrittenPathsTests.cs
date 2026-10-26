using Shouldly;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Unit and negative tests for <see cref="WrittenPaths" />, over hand-built fences and path sets rather
///     than the committed walkthroughs.
/// </summary>
public sealed class WrittenPathsTests
{
    [Fact]
    public void Extract_FencedWriteReport_CapturedWithLocationAndParts()
    {
        // Arrange
        string[] lines =
        [
            "intro prose",
            "```text",
            "$ sliderule render examples/Meridian/Meridian.slnx",
            "unchanged src/Meridian.Web/AGENTS.md",
            "```",
            "wrote arch/baselines/outside.json"
        ];
        string doc = string.Join("\n", lines);

        // Act
        IReadOnlyList<WrittenPath> written = WrittenPaths.Extract("d.md", doc);

        // Assert: the line outside the fence is prose restating the capture, not the capture itself.
        written.ShouldHaveSingleItem()
            .ShouldBe(new WrittenPath("d.md", 4, "unchanged", "src/Meridian.Web/AGENTS.md"));
    }

    [Theory]
    [InlineData("wrote")]
    [InlineData("unchanged")]
    public void Extract_EitherReportVerb_Captured(string label)
    {
        // Arrange
        string doc = string.Join(
            "\n",
            "```text",
            $"{label} arch/baselines/time/inject-clock.json",
            "```");

        // Act
        IReadOnlyList<WrittenPath> written = WrittenPaths.Extract("d.md", doc);

        // Assert
        written.ShouldHaveSingleItem()
            .Label.ShouldBe(label);
    }

    [Fact]
    public void Extract_FencedProseWithNoPathToken_Ignored()
    {
        // Arrange: a fenced line can open with the verb and still name no file. Without the narrowing
        // guard this would be reported as a stranded path forever, because no file can ever match it.
        string doc = string.Join(
            "\n",
            "```text",
            "unchanged behaviour",
            "wrote nothing",
            "```");

        // Act
        IReadOnlyList<WrittenPath> written = WrittenPaths.Extract("d.md", doc);

        // Assert
        written.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_MultipleFencedBlocks_AllPathsCaptured()
    {
        // Arrange: the second block reports the root card with no directory at all, and the extension is what
        // makes it a path rather than prose.
        string[] lines =
        [
            "```text",
            "wrote arch/baselines/data-access/no-inline-sql.json",
            "```",
            "prose between blocks",
            "~~~",
            "unchanged AGENTS.md",
            "~~~"
        ];
        string doc = string.Join("\n", lines);

        // Act
        IReadOnlyList<WrittenPath> written = WrittenPaths.Extract("d.md", doc);

        // Assert
        written.Select(static path => (path.DocLine, path.Path))
            .ShouldBe([(2, "arch/baselines/data-access/no-inline-sql.json"), (6, "AGENTS.md")]);
    }

    [Fact]
    public void Classify_TrackedUnderTheExampleRoot_Matched()
    {
        // Arrange
        WrittenPath written = new("d.md", 12, "unchanged", "src/Meridian.Web/AGENTS.md");
        IReadOnlySet<string> tracked = WrittenPaths.TrackedSet(["examples/Meridian/src/Meridian.Web/AGENTS.md"]);

        // Act
        string? failure = WrittenPaths.Classify(written, "examples/Meridian", tracked);

        // Assert
        failure.ShouldBeNull();
    }

    [Fact]
    public void Classify_EmptyExampleRoot_ResolvesAgainstTheRepositoryRoot()
    {
        // Arrange: a doc quoting a run over this repository's own solution reports paths that are already
        // repository-relative.
        WrittenPath written = new("README.md", 12, "wrote", "arch/baselines/mcp/env-through-seam.json");
        IReadOnlySet<string> tracked = WrittenPaths.TrackedSet(["arch/baselines/mcp/env-through-seam.json"]);

        // Act
        string? failure = WrittenPaths.Classify(written, string.Empty, tracked);

        // Assert
        failure.ShouldBeNull();
    }

    [Fact]
    public void Classify_RenamedFile_Untracked()
    {
        // Arrange: the rename this gate exists to catch — the walkthrough still quotes the old name.
        WrittenPath written = new("d.md", 12, "wrote", "arch/baselines/time/inject-clock.json");
        IReadOnlySet<string> tracked = WrittenPaths.TrackedSet(["examples/Meridian/arch/baselines/time/injected-clock.json"]);

        // Act
        string? failure = WrittenPaths.Classify(written, "examples/Meridian", tracked);

        // Assert: the failure names the doc, the line, the quoted line and the path that was looked for.
        string reported = failure.ShouldNotBeNull();
        reported.ShouldContain("d.md:12");
        reported.ShouldContain("wrote arch/baselines/time/inject-clock.json");
        reported.ShouldContain("examples/Meridian/arch/baselines/time/inject-clock.json");
    }

    [Fact]
    public void Classify_PathTrackedUnderADifferentExample_Untracked()
    {
        // Arrange: the root is part of the identity, so a sibling example's file cannot answer for this
        // walkthrough's quoted path.
        WrittenPath written = new("d.md", 12, "unchanged", "AGENTS.md");
        IReadOnlySet<string> tracked = WrittenPaths.TrackedSet(["examples/Meridian.Quoting/AGENTS.md"]);

        // Act
        string? failure = WrittenPaths.Classify(written, "examples/Meridian", tracked);

        // Assert
        failure.ShouldNotBeNull();
    }
}
