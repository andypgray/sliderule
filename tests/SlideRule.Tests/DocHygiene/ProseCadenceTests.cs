using Shouldly;
using Xunit;
using ContrastFrame = SlideRule.Tests.DocHygiene.ProseCadence.ContrastFrame;
using MeasuredSentence = SlideRule.Tests.DocHygiene.ProseCadence.MeasuredSentence;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Unit and negative tests for <see cref="ProseCadence" />, on inline text, so every sentence budget and
///     every contrast-frame rule is proved able to fail without a reader doc having to.
/// </summary>
public sealed class ProseCadenceTests
{
    [Fact]
    public void Sentences_HandWrappedParagraph_IsOneSentenceOnItsFirstLine()
    {
        // Arrange
        const string markdown =
            """
            The checker reads every project in the solution,
            resolves each reference it finds against the spec,
            and reports the first edge that breaks a named rule.
            """;

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(
                1,
                26,
                false,
                "The checker reads every project in the solution, resolves each reference it finds against the "
                + "spec, and reports the first edge that breaks a named rule."));
    }

    [Fact]
    public void Sentences_EachSentence_NamesTheLineItsFirstWordIsOn()
    {
        // Arrange
        const string markdown =
            """
            The first sentence is short. The second one starts on line one
            and wraps onto line two before it ends.
            The third starts on line three.
            """;

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        int[] lines = sentences
            .Select(static sentence => sentence.Line)
            .ToArray();
        int[] words = sentences
            .Select(static sentence => sentence.Words)
            .ToArray();
        lines.ShouldBe([1, 1, 3]);
        words.ShouldBe([5, 15, 6]);
    }

    [Fact]
    public void Sentences_FencedBlock_IsNotProseAndEndsTheParagraph()
    {
        // Arrange: the fenced line carries a colon and runs past twenty-five words, so it would report on
        // both budgets if it were measured, and dropping the fence rather than emptying it would join the
        // two sentences around it into one paragraph with a counted colon.
        const string markdown =
            """
            The check prints:
            ```text
            Rules: every rule in the spec ran against every project in the solution, and this quoted line would count as one sentence running well past twenty-five words.
            ```
            That is the whole report.
            """;

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldBe(
        [
            new MeasuredSentence(1, 3, false, "The check prints:"),
            new MeasuredSentence(5, 5, false, "That is the whole report.")
        ]);
    }

    [Fact]
    public void Sentences_HeadingCommentAndTableRows_AreNotProse()
    {
        // Arrange: the heading and the comment each carry a colon, the comment's second line would read as
        // a sentence if the comment were dropped only where its opening mark starts a line, and the table
        // row holds a sentence past twenty-five words.
        const string markdown =
            """
            # Rules: what the checker holds

            <!-- Maintainers: the table below is generated,
            so edit the spec that writes it. -->
            | Rule | What it holds |
            |---|---|
            | layering | The Domain layer must not reference the Application layer, the Infrastructure layer or the Api layer, since everything else in the subsystem is built on the Domain layer. |

            Each rule runs as a named test.
            """;

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(9, 7, false, "Each rule runs as a named test."));
    }

    [Fact]
    public void Sentences_FewerThanThreeWords_IsNotCounted()
    {
        // Arrange
        const string markdown = "Done. The check passes today.";

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(1, 4, false, "The check passes today."));
    }

    [Fact]
    public void Sentences_AbbreviationBeforeALowercaseWord_DoesNotEndTheSentence()
    {
        // Arrange
        const string markdown = "Name a place, e.g. the Domain layer, and every rule can target it.";

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(1, 13, false, markdown));
    }

    [Fact]
    public void Sentences_CodeSpan_IsOneWordThatCanOpenASentenceAndIsShownAsWritten()
    {
        // Arrange: the second sentence is twenty-six words with the code span counted as one.
        const string markdown =
            """
            The gate runs first. `check` then reads every rule in the spec and reports each edge that breaks one, naming the file and the line to edit for you today.
            """;

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldBe(
        [
            new MeasuredSentence(1, 4, false, "The gate runs first."),
            new MeasuredSentence(
                1,
                26,
                false,
                "`check` then reads every rule in the spec and reports each edge that breaks one, naming the file "
                + "and the line to edit for you today.")
        ]);
    }

    [Fact]
    public void Sentences_LinkLedSentence_RunsOnFromTheOneBefore()
    {
        // Arrange: a sentence break needs a capital, a quote, a parenthesis or an asterisk after the
        // whitespace, and a bracket is none of them.
        const string markdown = "The gate runs first. [The report](#report) names every edge.";

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(1, 9, false, "The gate runs first. [The report] names every edge."));
    }

    [Theory]
    [InlineData("- The listed rule holds here.")]
    [InlineData("1. The listed rule holds here.")]
    [InlineData("> The listed rule holds here.")]
    public void Sentences_ListOrQuoteMarker_IsNotAWord(string markdown)
    {
        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(1, 5, false, "The listed rule holds here."));
    }

    [Fact]
    public void Sentences_ColonMidSentence_IsCounted()
    {
        // Arrange
        const string markdown = "The architecture is real: layers, boundaries and rules.";

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(1, 8, true, markdown));
    }

    [Fact]
    public void Sentences_ColonInsideATokenOrCodeSpan_IsNotCounted()
    {
        // Arrange: a colon counts only when whitespace and more of the sentence follow it.
        const string markdown = "Read `file:line` from the report at https://example.test/a.b today.";

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(1, 8, false, markdown));
    }

    [Fact]
    public void Sentences_ColonEndingALineBeforeAListItem_IsNotCounted()
    {
        // Arrange: the list is tight and its items carry no end punctuation, so the list runs on from its
        // introduction as one sentence, and the introducing colon is the only colon in it.
        const string markdown =
            """
            The report names three things:
            - the rule that broke,
            - the edge that broke it,
            - and the line to edit.
            """;

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(
                1,
                19,
                false,
                "The report names three things: the rule that broke, the edge that broke it, and the line to edit."));
    }

    [Fact]
    public void Sentences_ColonEndingALineBeforeProse_IsCounted()
    {
        // Arrange
        const string markdown =
            """
            The report names three things:
            the rule, the edge and the line.
            """;

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(1, 12, true, "The report names three things: the rule, the edge and the line."));
    }

    [Theory]
    [InlineData(
        "- **Enforcement**: one checker passes or fails the rules.",
        8,
        "**Enforcement**: one checker passes or fails the rules.")]
    [InlineData(
        "- `check` (the verb): reads the spec and the solution.",
        9,
        "`check` (the verb): reads the spec and the solution.")]
    [InlineData(
        "1. [Status](#status): what ships today and what does not.",
        8,
        "[Status]: what ships today and what does not.")]
    public void Sentences_NameAndDescriptionListItem_DoesNotCountItsColon(string markdown, int words, string text)
    {
        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(1, words, false, text));
    }

    [Fact]
    public void Sentences_NameAndDescriptionOutsideAList_CountsItsColon()
    {
        // Arrange
        const string markdown = "**Enforcement**: one checker passes or fails the rules.";

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(1, 8, true, markdown));
    }

    [Fact]
    public void Sentences_SecondColonInANameAndDescriptionItem_IsCounted()
    {
        // Arrange
        const string markdown = "- **Enforcement**: one checker runs everywhere: in CI and in a hook.";

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe(new MeasuredSentence(1, 11, true, "**Enforcement**: one checker runs everywhere: in CI and in a hook."));
    }

    [Fact]
    public void Sentences_NameLedTightList_CountsOnlyTheColonInADescription()
    {
        // Arrange: the opening of a tight list whose items open with a link and a name colon, the second item
        // cut after its first sentence. A link-led item runs on from the sentence before it, so the second
        // item joins the last sentence of the first, and of the three colons only the one inside a
        // description counts.
        const string markdown =
            """
            - [Three of the four postures on one codebase](Meridian/): the `Meridian` monolith mid-migration.
              The law it keeps, a ratcheted migration with its burndown, and a quarantined scope with dragons.
              Its [drawings](Meridian/ARCHITECTURE.md) put those three postures on one page as shapes: a solid
              ban, two dotted debt arrows, and a box you can only enter through its facades.
            - [Enforce-only clean architecture](Meridian.Quoting/): the greenfield `Meridian.Quoting`
              subsystem.
            """;

        // Act
        IReadOnlyList<MeasuredSentence> sentences = ProseCadence.Sentences(markdown);

        // Assert
        sentences.ShouldBe(
        [
            new MeasuredSentence(
                1,
                12,
                false,
                "[Three of the four postures on one codebase]: the `Meridian` monolith mid-migration."),
            new MeasuredSentence(
                2,
                16,
                false,
                "The law it keeps, a ratcheted migration with its burndown, and a quarantined scope with dragons."),
            new MeasuredSentence(
                3,
                35,
                true,
                "Its [drawings] put those three postures on one page as shapes: a solid ban, two dotted debt arrows, "
                + "and a box you can only enter through its facades. [Enforce-only clean architecture]: the "
                + "greenfield `Meridian.Quoting` subsystem.")
        ]);
    }

    [Fact]
    public void Findings_TwentyFiveWordSentence_ReportsNothing()
    {
        // Arrange
        const string markdown =
            """
            Enforcement covers the checker at the command line, in CI, as named xUnit tests, and in an agent hook when the agent's turn finally ends.
            """;

        // Act
        IReadOnlyList<string> findings = ProseCadence.Findings(markdown);

        // Assert
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Findings_TwentySixWordSentence_ReportsItWithItsLineAndLength()
    {
        // Arrange
        const string markdown =
            """
            Enforcement covers the checker at the command line, in CI, as named xUnit tests, and in an agent hook when the agent's turn finally ends today.
            """;

        // Act
        IReadOnlyList<string> findings = ProseCadence.Findings(markdown);

        // Assert
        findings.ShouldBe(
        [
            "1 of 1 sentences run past 25 words (at most 0 may)",
            "L1: 26 words: Enforcement covers the checker at the command line, in CI, as named xUnit tests, and in "
            + "an agent hook when the agent's turn finally ends today."
        ]);
    }

    [Fact]
    public void Findings_OneLongSentenceInFive_ReportsNothing()
    {
        // Arrange
        const string markdown =
            """
            One rule holds.
            Two rules hold.
            Three rules hold.
            Four rules hold.
            Enforcement covers the checker at the command line, in CI, as named xUnit tests, and in an agent hook when the agent's turn finally ends today.
            """;

        // Act
        IReadOnlyList<string> findings = ProseCadence.Findings(markdown);

        // Assert
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Findings_TwoLongSentencesInFive_ReportsBothUnderTheCount()
    {
        // Arrange
        const string markdown =
            """
            One rule holds.
            Two rules hold.
            Three rules hold.
            Enforcement covers the checker at the command line, in CI, as named xUnit tests, and in an agent hook when the agent's turn finally ends today.
            Agent context covers the managed block, the per-directory rule cards, and the query tools that an agent calls before it edits any file in the repository.
            """;

        // Act
        IReadOnlyList<string> findings = ProseCadence.Findings(markdown);

        // Assert
        findings.ShouldBe(
        [
            "2 of 5 sentences run past 25 words (at most 1 may)",
            "L4: 26 words: Enforcement covers the checker at the command line, in CI, as named xUnit tests, and in "
            + "an agent hook when the agent's turn finally ends today.",
            "L5: 26 words: Agent context covers the managed block, the per-directory rule cards, and the query tools "
            + "that an agent calls before it edits any file in the repository."
        ]);
    }

    [Fact]
    public void Findings_OneColonInTen_ReportsNothing()
    {
        // Arrange
        const string markdown =
            """
            One rule holds.
            Two rules hold.
            Three rules hold.
            Four rules hold.
            Five rules hold.
            Six rules hold.
            Seven rules hold.
            Eight rules hold.
            Nine rules hold.
            The architecture is real: layers, boundaries and rules.
            """;

        // Act
        IReadOnlyList<string> findings = ProseCadence.Findings(markdown);

        // Assert
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Findings_OneColonInNine_ReportsItUnderTheCount()
    {
        // Arrange
        const string markdown =
            """
            One rule holds.
            Two rules hold.
            Three rules hold.
            Four rules hold.
            Five rules hold.
            Six rules hold.
            Seven rules hold.
            Eight rules hold.
            The architecture is real: layers, boundaries and rules.
            """;

        // Act
        IReadOnlyList<string> findings = ProseCadence.Findings(markdown);

        // Assert
        findings.ShouldBe(
        [
            "1 of 9 sentences carry a colon mid-sentence (at most 0 may)",
            "L9: colon: The architecture is real: layers, boundaries and rules."
        ]);
    }

    [Fact]
    public void FrameFindings_SamePhraseInTwoSections_ReportsEveryUse()
    {
        // Arrange
        const string markdown =
            """
            # Checking

            The checker reads references rather than names.

            ## Rendering

            The renderer writes prose rather than tables.
            """;

        // Act
        IReadOnlyList<string> findings = ProseCadence.FrameFindings(markdown);

        // Assert
        findings.ShouldBe(
        [
            "L3: \"rather than\", 2 times in the doc: rather than",
            "L7: \"rather than\", 2 times in the doc: rather than"
        ]);
    }

    [Fact]
    public void FrameFindings_OneFrameInEachOfTwoSections_ReportsNothing()
    {
        // Arrange
        const string markdown =
            """
            # Checking

            The checker reads references rather than names.

            ## Rendering

            The renderer writes not only prose but tables.
            """;

        // Act
        IReadOnlyList<string> findings = ProseCadence.FrameFindings(markdown);

        // Assert
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void FrameFindings_TwoFramesInOneSection_ReportsBoth()
    {
        // Arrange
        const string markdown =
            """
            # Checking

            The checker reads references rather than names.
            It checks not only projects but namespaces.
            """;

        // Act
        IReadOnlyList<string> findings = ProseCadence.FrameFindings(markdown);

        // Assert
        findings.ShouldBe(
        [
            "L3: 2 frames under \"# Checking\": rather than",
            "L4: 2 frames under \"# Checking\": not only"
        ]);
    }

    [Fact]
    public void FrameFindings_StagedNegationWordedTwoWays_IsOneKindUsedTwice()
    {
        // Arrange
        const string markdown =
            """
            # Rules

            A rule isn't a comment, it's a check.

            ## Fixes

            A fix is not a hint, but a step.
            """;

        // Act
        IReadOnlyList<string> findings = ProseCadence.FrameFindings(markdown);

        // Assert
        findings.ShouldBe(
        [
            "L3: \"isn't X, it's Y\", 2 times in the doc: isn't a comment, it's",
            "L7: \"isn't X, it's Y\", 2 times in the doc: is not a hint, but"
        ]);
    }

    [Fact]
    public void FrameFindings_FramesAboveTheFirstHeading_ShareOneSection()
    {
        // Arrange
        const string markdown =
            """
            The checker reads references rather than names.
            It checks not only projects but namespaces.

            # Checking
            """;

        // Act
        IReadOnlyList<string> findings = ProseCadence.FrameFindings(markdown);

        // Assert
        findings.ShouldBe(
        [
            "L1: 2 frames above the first heading: rather than",
            "L2: 2 frames above the first heading: not only"
        ]);
    }

    [Fact]
    public void Frames_InHeadingCodeSpanOrFence_AreNotCounted()
    {
        // Arrange
        const string markdown =
            """
            # Rather than guessing

            Pass `not only` as the argument.

            ```text
            rather than
            ```
            """;

        // Act
        IReadOnlyList<ContrastFrame> frames = ProseCadence.Frames(markdown);

        // Assert
        frames.ShouldBeEmpty();
    }

    [Fact]
    public void Frames_NegationWithAPivot_IsTheStagedKindEvenAfterNotJust()
    {
        // Arrange: the staged negation starts at "is not", before "not just" can, and runs through the pivot,
        // so the one frame here is the staged kind.
        const string markdown = "It is not just a linter, but a gate.";

        // Act
        IReadOnlyList<ContrastFrame> frames = ProseCadence.Frames(markdown);

        // Assert
        frames.ShouldHaveSingleItem()
            .ShouldBe(new ContrastFrame(1, "isn't X, it's Y", 0, string.Empty, "is not just a linter, but"));
    }
}
