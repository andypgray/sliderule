using Shouldly;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Unit and negative tests for <see cref="FirstScreenShape" />, on inline text, so the gate is proved
///     able to fail without the landing page having to.
/// </summary>
public sealed class FirstScreenShapeTests
{
    private const char EmDash = (char)0x2014;

    /// <summary>
    ///     The landing page's first screen as it read before the rewrite this gate protects: a colon
    ///     expansion in most sentences and two sentences past the word budget. Quoted verbatim through
    ///     the closing line of the fold, fence included, so the line numbers the findings name are the
    ///     numbers that text had in the file.
    /// </summary>
    private const string PreRewriteFirstScreen =
        """
        # SlideRule

        <!-- mcp-name: io.github.andypgray/sliderule -->

        [![CI](https://github.com/andypgray/sliderule/actions/workflows/ci.yml/badge.svg)](https://github.com/andypgray/sliderule/actions/workflows/ci.yml) [![OpenSSF Scorecard](https://img.shields.io/ossf-scorecard/github.com/andypgray/sliderule?label=openssf+scorecard)](https://scorecard.dev/viewer/?uri=github.com/andypgray/sliderule) [![NuGet](https://img.shields.io/nuget/v/SlideRule.Cli?logo=nuget&label=nuget)](https://www.nuget.org/packages/SlideRule.Cli) [![NuGet downloads](https://img.shields.io/nuget/dt/SlideRule.Cli?label=downloads)](https://www.nuget.org/packages/SlideRule.Cli)

        SlideRule is a .NET tool that renders one C# architecture spec to two targets: enforcement and agent context.

        The architecture of a long-lived codebase is real: layers, boundaries, rules. It is also unenforced: it lives in a few heads, no build step checks it, and diagrams drift. Nothing fails when a change crosses a boundary, least of all when a coding agent wrote the change: fast, plausible, and blind to which walls are structural. Architecture-as-code is SlideRule's answer: the rules become one C# spec, and the spec becomes every surface on this page.

        1. **Enforcement**: one checker passes or fails the rules at the command line, in CI, as named xUnit tests, and in an agent hook when the agent's turn ends.
        2. **Agent context**: the same rules render to a managed `AGENTS.md` block, per-directory rule cards, and MCP query tools for coding agents.

        Write your architecture once. Use it everywhere.

        A rule is one statement:

        ```csharp
        arch.Rule("layering/domain-independent")
            .Enforce(domain.MustNotReference(application, infrastructure, api))
            .Because("The Domain holds the quote and rate model the rest of the subsystem is built on; it stays free of the layers that depend on it so it can be reasoned about and tested on its own.")
            .Fix("Move the dependency out of Domain: define an interface here and implement it in the outer layer that needs it.");
        ```

        That is the whole rule: an ID, a posture (`Enforce`), a constraint, a reason, a fix. It is committed in the [clean-architecture example](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Quoting), and CI holds `check` green against the codebase it governs.

        SlideRule is pre-alpha and under construction; [Status](#status) holds the current inventory.
        """;

    [Fact]
    public void Findings_PreRewriteFirstScreen_ReportsEveryColonExpansionAndLongSentence()
    {
        // Act
        IReadOnlyList<string> findings = FirstScreenShape.Findings(PreRewriteFirstScreen);

        // Assert
        string[] longSentences = findings
            .Where(static finding => finding.Contains(" words: "))
            .ToArray();
        longSentences.ShouldBe(
        [
            "L9: 27 words: Nothing fails when a change crosses a boundary, least of all when a coding "
            + "agent wrote the change: fast, plausible, and blind to which walls are structural.",
            "L11: 28 words: Enforcement: one checker passes or fails the rules at the command line, in "
            + "CI, as named xUnit tests, and in an agent hook when the agent's turn ends."
        ]);

        string[] expansionLines = findings
            .Where(static finding => finding.Contains(": colon: "))
            .Select(static finding => finding[..finding.IndexOf(':')])
            .ToArray();
        expansionLines.ShouldBe(["L7", "L9", "L9", "L9", "L9", "L11", "L12", "L25"]);

        string[] emDashes = findings
            .Where(static finding => finding.Contains(": em-dash: "))
            .ToArray();
        emDashes.ShouldBeEmpty();

        findings.Count.ShouldBe(longSentences.Length + expansionLines.Length);
    }

    [Fact]
    public void Findings_LineFinalColonAndShortSentences_ReportsNothing()
    {
        // Arrange: the fenced rule carries both a mid-line colon and a sentence far past the budget, so
        // this holds only while fences come out of the measurement.
        const string page =
            """
            # SlideRule

            SlideRule is a .NET architecture checker that enforces one C# spec.

            A rule is one statement:

            ```csharp
            arch.Rule("layering/domain-independent")
                .Enforce(domain.MustNotReference(application, infrastructure, api))
                .Fix("Move the dependency out of Domain: define an interface here and implement it in the outer layer that needs it.");
            ```

            That is the whole rule. It has an ID, a posture, a constraint, a reason and a fix.

            ## One spec produces
            """;

        // Act
        IReadOnlyList<string> findings = FirstScreenShape.Findings(page);

        // Assert
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Sentences_DottedTokensCodeSpanAndLink_StayInsideOneSentence()
    {
        // Arrange
        const string line =
            "SlideRule targets .NET 1.0 and renders AGENTS.md from `x.y` as the "
            + "[guide](https://example.test/a.b) says.";

        // Act
        IReadOnlyList<string> sentences = FirstScreenShape.Sentences(line);

        // Assert
        sentences.ShouldHaveSingleItem()
            .ShouldBe("SlideRule targets .NET 1.0 and renders AGENTS.md from code as the guide says.");
    }

    [Fact]
    public void Findings_TitleCommentBadgeAndTableLines_AreNotProseAndNorIsAnythingBelowTheFold()
    {
        // Arrange: every line here but the one declarative sentence would report something if it were
        // measured — an em-dash on the title, the comment, the table row and the line under the heading,
        // and the colons of four badge URLs.
        var page =
            $"""
             # SlideRule {EmDash} the title line

             <!-- mcp-name: io.github.andypgray/sliderule {EmDash} metadata, not prose -->

             [![CI](https://example.test/ci/badge.svg)](https://example.test/ci) [![NuGet](https://example.test/nuget/badge.svg)](https://example.test/nuget)

             A rule is one statement.

             | Target | What it is |
             |---|---|
             | Enforcement | one checker {EmDash} at the command line, in CI: everywhere |

             ## One spec produces

             That is the whole rule: an ID, a posture, a constraint {EmDash} and a fix.
             """;

        // Act
        IReadOnlyList<string> findings = FirstScreenShape.Findings(page);

        // Assert
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Findings_MultiLineHtmlComment_IsFurnitureThroughItsClosingDelimiter()
    {
        // Arrange: a comment whose second and third lines would each report if the scanner read the
        // opening delimiter as a prefix and then forgot it. The one sentence below the comment is what
        // proves the fold is still being measured after the comment closes.
        var page =
            $"""
             # SlideRule

             <!--
             mcp-name: io.github.andypgray/sliderule {EmDash} metadata, not prose, and neither is the line under it.
             This registry block is long enough that a single sentence taken out of it would run well past the word budget the fold is held to by this gate.
             -->

             A rule is one statement.
             """;

        // Act
        IReadOnlyList<string> findings = FirstScreenShape.Findings(page);

        // Assert
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Findings_Blockquote_IsMeasuredWithoutItsMarkerRatherThanSkipped()
    {
        // Arrange: a quotation is prose a reader reads as sentences, so it is held to the same shape as
        // the paragraphs around it. Twenty-six words once the marker is gone; counting the marker would
        // make it twenty-seven.
        const string page =
            """
            > Enforcement covers the checker at the command line, in CI, as named xUnit tests, and in an agent hook when the agent's turn finally ends today.
            """;

        // Act
        IReadOnlyList<string> findings = FirstScreenShape.Findings(page);

        // Assert
        findings.ShouldHaveSingleItem()
            .ShouldBe(
                "L1: 26 words: Enforcement covers the checker at the command line, in CI, as named xUnit "
                + "tests, and in an agent hook when the agent's turn finally ends today.");
    }

    [Fact]
    public void Findings_NumberedListItem_IsMeasuredWithoutItsMarkerAndBoldMarkers()
    {
        // Arrange: twenty-six words once the marker and the bold markers are gone. Counting the marker
        // would make it twenty-seven, and splitting on it would make the rest a sentence of its own.
        const string page =
            """
            1. **Enforcement** covers the checker at the command line, in CI, as named xUnit tests, and in an agent hook when the agent's turn finally ends today.
            """;

        // Act
        IReadOnlyList<string> findings = FirstScreenShape.Findings(page);

        // Assert
        findings.ShouldHaveSingleItem()
            .ShouldBe(
                "L1: 26 words: Enforcement covers the checker at the command line, in CI, as named xUnit "
                + "tests, and in an agent hook when the agent's turn finally ends today.");
    }
}
