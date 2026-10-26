using Shouldly;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Unit and negative tests for <see cref="DocProse" />. They pin the fence blanking and the
///     markdown readings, and prove that each forbidden reference trips the same checker the
///     documentation gates run on.
/// </summary>
public sealed class DocProseTests
{
    [Fact]
    public void BlankFences_TildeFence_Emptied()
    {
        // Arrange
        string[] lines = ["keep one", "~~~", "drop me", "~~~", "keep two"];
        string text = string.Join("\n", lines);

        // Act
        string blanked = DocProse.BlankFences(text);

        // Assert
        blanked.ShouldContain("keep one");
        blanked.ShouldContain("keep two");
        blanked.ShouldNotContain("drop");
    }

    [Fact]
    public void BlankFences_ClosingFenceLongerThanOpener_Closes()
    {
        // Arrange
        string[] lines = ["a", "```", "inside", "`````", "b"];
        string text = string.Join("\n", lines);

        // Act
        string blanked = DocProse.BlankFences(text);

        // Assert
        blanked.ShouldContain("a");
        blanked.ShouldContain("b");
        blanked.ShouldNotContain("inside");
    }

    [Fact]
    public void BlankFences_ShorterRunOfFenceCharacter_DoesNotClose()
    {
        // Arrange: a three-backtick line cannot close a four-backtick fence, so the block runs on to
        // the four-backtick line and everything between it and the opener is blanked.
        string[] lines = ["a", "````", "inside", "```", "still inside", "````", "b"];
        string text = string.Join("\n", lines);

        // Act
        string blanked = DocProse.BlankFences(text);

        // Assert
        blanked.ShouldContain("a");
        blanked.ShouldContain("b");
        blanked.ShouldNotContain("inside");
        blanked.ShouldNotContain("still inside");
    }

    [Fact]
    public void BlankFences_UnclosedFence_EmptiesToEndOfText()
    {
        // Arrange
        string[] lines = ["keep this", "```", "dropped to the end", "also dropped"];
        string text = string.Join("\n", lines);

        // Act
        string blanked = DocProse.BlankFences(text);

        // Assert
        blanked.ShouldContain("keep this");
        blanked.ShouldNotContain("dropped");
    }

    [Theory]
    [InlineData("```", "\n")]
    [InlineData("```text", "\r\n")]
    public void BlankFences_FencedBlock_EmptiesItsLinesAndKeepsEveryLineNumber(string opener, string newline)
    {
        // Arrange: the CRLF row also shows the newlines coming back as "\n".
        string[] lines = ["before", opener, "inside", "```", "after"];
        string text = string.Join(newline, lines);

        // Act
        string blanked = DocProse.BlankFences(text);

        // Assert
        blanked.ShouldBe("before\n\n\n\nafter");
    }

    [Fact]
    public void PlainText_HardWrappedLinkAndCodeSpan_ReadAsOneLineOfTheirText()
    {
        // Arrange: a readme's opening as a package's readme spells it, wrapped mid-sentence with CRLF.
        string[] lines =
        [
            "`SlideRule.Xunit` is the [SlideRule](https://github.com/andypgray/sliderule) xUnit",
            "adapter: every rule runs as a test."
        ];
        string markdown = string.Join("\r\n", lines);

        // Act
        string plain = DocProse.PlainText(markdown);

        // Assert
        plain.ShouldBe("SlideRule.Xunit is the SlideRule xUnit adapter: every rule runs as a test.");
    }

    [Fact]
    public void PlainText_CodeSpanInsideLinkText_LosesItsBackticks()
    {
        // Arrange
        const string markdown = "Reference [`SlideRule`](https://www.nuget.org/packages/SlideRule) alone.";

        // Act
        string plain = DocProse.PlainText(markdown);

        // Assert
        plain.ShouldBe("Reference SlideRule alone.");
    }

    [Fact]
    public void PlainText_NamespaceWildcard_Kept()
    {
        // Arrange
        const string markdown = "Layers are patterns such as `MyApp.Domain.*`.";

        // Act
        string plain = DocProse.PlainText(markdown);

        // Assert
        plain.ShouldBe("Layers are patterns such as MyApp.Domain.*.");
    }

    [Theory]
    [InlineData("Phase 12")]
    [InlineData("Phase-4")]
    [InlineData("WP3")]
    [InlineData("WP 3")]
    [InlineData("WP-7")]
    [InlineData("WP")]
    [InlineData("this phase")]
    [InlineData("the current phase")]
    [InlineData("that final phase")]
    [InlineData("task-131")]
    [InlineData("work package")]
    [InlineData("tracker")]
    [InlineData("DESIGN.md")]
    [InlineData("PLAN.md")]
    [InlineData("PLAN-ARCHIVE.md")]
    [InlineData("EXAMPLES.md")]
    [InlineData("GUIDANCE-PACK.md")]
    [InlineData("EVALUATION.md")]
    [InlineData("outside-review.md")]
    [InlineData("docs/notes.md")]
    [InlineData("[notes](docs/notes.md)")]
    [InlineData("../docs/notes.md")]
    [InlineData("https://github.com/andypgray/sliderule/blob/main/docs/notes.md")]
    [InlineData("https://raw.githubusercontent.com/andypgray/sliderule/main/docs/notes.md")]
    public void FindForbidden_ForbiddenTokenOnSecondLine_ReportsHitWithLineNumber(string token)
    {
        // Arrange
        var text = $"first clean line\nprefix {token} suffix\nthird clean line";

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(text, DocHygieneTests.InternalReferencePatterns);

        // Assert
        hits.ShouldNotBeEmpty();
        hits.ShouldAllBe(hit => hit.StartsWith("2: "));
    }

    [Theory]
    [InlineData("a phase 9 lowercase note")]
    [InlineData("phase two, without a digit")]
    [InlineData("see PLAN.mdx for details")]
    [InlineData("the EXAMPLES.mdown file")]
    [InlineData("WPF data bindings")]
    [InlineData("two-phase ready tasks")]
    [InlineData("internal phase and work-item labels")]
    [InlineData("the phased rollout continues")]
    [InlineData("Task-returning methods await their result")]
    [InlineData("a sweep of a large backlog")]
    [InlineData("the guidance-pack spec")]
    [InlineData("the twenty-rule board")]
    [InlineData("https://code.claude.com/docs/en/memory")]
    [InlineData("https://github.com/modelcontextprotocol/registry/blob/main/docs/README.md")]
    [InlineData("https://github.com/andypgray/sliderule/blob/main/src/README.md")]
    public void FindForbidden_BenignLookalike_ReportsNoHit(string text)
    {
        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(text, DocHygieneTests.InternalReferencePatterns);

        // Assert
        hits.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(@"C:\Users\someone\notes.txt")]
    [InlineData("D:/repos/a-sibling")]
    [InlineData("/home/runner/work")]
    [InlineData("/Users/someone/dev")]
    [InlineData(@"%USERPROFILE%\.nuget")]
    [InlineData("someone@gmail.com")]
    public void FindForbidden_PrivateEnvironmentTokenOnSecondLine_ReportsHitWithLineNumber(string token)
    {
        // Arrange
        var text = $"first clean line\nprefix {token} suffix\nthird clean line";

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(text, DocHygieneTests.PrivateEnvironmentPatterns);

        // Assert
        hits.ShouldNotBeEmpty();
        hits.ShouldAllBe(hit => hit.StartsWith("2: "));
    }

    [Theory]
    [InlineData(@"%LOCALAPPDATA%\SlideRule\logs")]
    [InlineData(@"C:\Program Files\Microsoft Visual Studio")]
    [InlineData("\"C:/pkgs/library.dll\"")]
    [InlineData("\"C:/out/build\"")]
    [InlineData("https://github.com/andypgray/sliderule")]
    [InlineData("noreply@example.invalid")]
    [InlineData("the homepage link")]
    public void FindForbidden_PrivateEnvironmentLookalike_ReportsNoHit(string text)
    {
        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(text, DocHygieneTests.PrivateEnvironmentPatterns);

        // Assert
        hits.ShouldBeEmpty();
    }
}
