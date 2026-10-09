using Shouldly;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Unit and negative tests for <see cref="DocProse" />. They pin the fence blanking and the
///     prose measurements, and prove that each forbidden reference and off-voice word trips the same
///     checker the documentation gates run on.
/// </summary>
public sealed class DocProseTests
{
    private const char EmDash = (char)0x2014;

    [Fact]
    public void BlankFences_FencedBlock_EmptiesFenceAndKeepsSurroundingProse()
    {
        // Arrange
        string[] lines = [$"before {EmDash} one", "```", $"inside {EmDash} fenced", "```", $"after {EmDash} two"];
        string text = string.Join("\n", lines);

        // Act
        string blanked = DocProse.BlankFences(text);

        // Assert
        blanked.ShouldContain("before");
        blanked.ShouldContain("after");
        blanked.ShouldNotContain("inside");
        DocProse.CountEmDashes(blanked)
            .ShouldBe(2);
    }

    [Fact]
    public void BlankFences_TildeFence_Emptied()
    {
        // Arrange
        string[] lines = ["keep one", "~~~", $"drop {EmDash} me", "~~~", "keep two"];
        string text = string.Join("\n", lines);

        // Act
        string blanked = DocProse.BlankFences(text);

        // Assert
        blanked.ShouldContain("keep one");
        blanked.ShouldContain("keep two");
        blanked.ShouldNotContain("drop");
        DocProse.CountEmDashes(blanked)
            .ShouldBe(0);
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

    [Fact]
    public void BlankFences_CrlfInput_RecognizesFences()
    {
        // Arrange
        string text = "before\r\n```\r\ninside " + EmDash + " x\r\n```\r\nafter";

        // Act
        string blanked = DocProse.BlankFences(text);

        // Assert
        blanked.ShouldContain("before");
        blanked.ShouldContain("after");
        blanked.ShouldNotContain("inside");
        DocProse.CountEmDashes(blanked)
            .ShouldBe(0);
    }

    [Fact]
    public void BlankFences_FencedBlock_EmptiesItsLinesAndKeepsEveryLineNumber()
    {
        // Arrange: CRLF input, so the result also shows the newlines coming back as "\n".
        string[] lines = ["before", "```text", "inside", "```", "after"];
        string text = string.Join("\r\n", lines);

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

    [Fact]
    public void CountWords_WhitespaceSeparatedTokens_CountsRuns()
    {
        // Arrange
        var text = "  leading and   multiple\tspaces\nand newline ";

        // Act
        int words = DocProse.CountWords(text);

        // Assert
        words.ShouldBe(6);
    }

    [Fact]
    public void CountEmDashes_EmDashOccurrences_Counted()
    {
        // Arrange
        var withDashes = $"a{EmDash}b{EmDash}c and none here";

        // Act & Assert
        DocProse.CountEmDashes(withDashes)
            .ShouldBe(2);
        DocProse.CountEmDashes("no dashes at all")
            .ShouldBe(0);
    }

    [Fact]
    public void CountTics_MixedCase_CountedCaseInsensitively()
    {
        // Act & Assert
        DocProse.CountTics("This is deliberately here and intentionally there")
            .ShouldBe(2);
        DocProse.CountTics("Deliberately capitalised and INTENTIONALLY shouted")
            .ShouldBe(2);
        DocProse.CountTics("nothing of the sort")
            .ShouldBe(0);
    }

    [Fact]
    public void Budget_OneEmDashInFiveHundredWords_WithinCeiling()
    {
        // Arrange: 500 words carrying exactly one em-dash, whose ceiling budget is one.
        string text = string.Join(" ", Enumerable.Repeat("word", 499)) + $" one{EmDash}dash";

        // Act
        int words = DocProse.CountWords(text);
        int emDashes = DocProse.CountEmDashes(text);
        var budget = (int)Math.Ceiling(words / 1000.0);

        // Assert
        words.ShouldBe(500);
        emDashes.ShouldBe(1);
        emDashes.ShouldBeLessThanOrEqualTo(budget);
    }

    [Fact]
    public void Budget_TwoEmDashesWhenCeilingIsOne_ExceedsBudget()
    {
        // Arrange: 500 words carrying two em-dashes, whose ceiling budget is still one.
        string text = string.Join(" ", Enumerable.Repeat("word", 498)) + $" one{EmDash}dash two{EmDash}dash";

        // Act
        int words = DocProse.CountWords(text);
        int emDashes = DocProse.CountEmDashes(text);
        var budget = (int)Math.Ceiling(words / 1000.0);

        // Assert
        words.ShouldBe(500);
        emDashes.ShouldBe(2);
        emDashes.ShouldBeGreaterThan(budget);
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

    [Fact]
    public void FindForbidden_OffVoiceWord_ReportsHitWithLineNumber()
    {
        // Arrange
        var text = "The documentation reads clearly.\nThe codebase is messy today.";

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(text, DocHygieneTests.HouseVoicePatterns);

        // Assert
        hits.ShouldHaveSingleItem()
            .ShouldBe("2: messy");
    }

    [Theory]
    [InlineData("the files were arranged messily")]
    [InlineData("open the messyroom door")]
    public void FindForbidden_OffVoiceLookalike_ReportsNoHit(string text)
    {
        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(text, DocHygieneTests.HouseVoicePatterns);

        // Assert
        hits.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("It runs quietly in CI.", "quietly")]
    [InlineData("The gate is genuinely strict.", "genuinely")]
    [InlineData("To be honest, the fence is long.", "honest")]
    [InlineData("The pair is honestly scoped.", "honest")]
    [InlineData("Frankly, nobody reads it.", "Frankly")]
    [InlineData("The check earns its keep.", "earns its keep")]
    [InlineData("Both tables earn their place.", "earn their place")]
    [InlineData("It is worth noting that the order matters.", "worth noting")]
    [InlineData("It is worth stating once.", "worth stating")]
    [InlineData("One spec, full stop.", "full stop")]
    [InlineData("That is the real reason.", "the real reason")]
    [InlineData("Here's how it works.", "Here's how")]
    [InlineData("The sentence carries the argument.", "carries the argument")]
    [InlineData("Two layers carry the weight.", "carry the weight")]
    [InlineData("It compiles, and the trap closes.", "and the trap")]
    public void FindForbidden_StockPhraseOnSecondLine_ReportsHitWithLineNumber(string sentence, string phrase)
    {
        // Arrange
        var text = $"first clean line\n{sentence}\nthird clean line";

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(text, DocHygieneTests.StockPhrasePatterns);

        // Assert
        hits.ShouldHaveSingleItem()
            .ShouldBe($"2: {phrase}");
    }

    [Theory]
    [InlineData("It runs against a real build.")]
    [InlineData("The rule earned its place in the pack.")]
    [InlineData("There's the catch.")]
    [InlineData("Understand the trap before you set it.")]
    [InlineData("The dishonest baseline is refused.")]
    [InlineData("Nobody learns its place from a table.")]
    [InlineData("The realm of the spec is small.")]
    [InlineData("It carried the weight alone.")]
    public void FindForbidden_StockPhraseLookalike_ReportsNoHit(string text)
    {
        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(text, DocHygieneTests.StockPhrasePatterns);

        // Assert
        hits.ShouldBeEmpty();
    }

    [Fact]
    public void FindForbidden_StockPhraseInsideABlankedFence_IsExempt()
    {
        // Arrange: the fenced line carries a stock phrase and the last line carries one, so a single hit on
        // line 5 shows both that the fence is exempt and that blanking it moved no line.
        const string doc =
            """
            The rule holds.
            ```
            It quietly skips the rest.
            ```
            The pair is honestly scoped.
            """;
        string prose = DocProse.BlankFences(doc);

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(prose, DocHygieneTests.StockPhrasePatterns);

        // Assert
        hits.ShouldHaveSingleItem()
            .ShouldBe("5: honest");
    }

    [Fact]
    public void FindForbidden_DesignTermWrappedInsideBold_IsBlankedWhileABareStockPhraseStillReports()
    {
        // Arrange: the term wraps across lines 1 and 2 inside bold, and line 3 carries a bare stock phrase, so a
        // single hit on line 3 shows both that the wrapped term is exempt and that blanking it moved no line.
        const string doc =
            """
            Reflection is the documented **honesty
              boundary**, invisible either way, and the
            honest shape is a baseline.
            """;
        string prose = DocProse.BlankMatches(DocProse.BlankFences(doc), DocHygieneTests.DesignTermPatterns);

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(prose, DocHygieneTests.StockPhrasePatterns);

        // Assert
        hits.ShouldHaveSingleItem()
            .ShouldBe("3: honest");
    }
}
