using System.Text.Json;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end <c>graph</c> against the LinkedApp fixture — the one solution in the suite that compiles
///     a single source file into more than one project, which is the shape neither MyApp nor any synthetic
///     compilation can produce: <c>LinkedApp.Tool</c> carries
///     <c>&lt;Compile Include="..\LinkedApp.Core\Shared\Widget.cs"&gt;</c> and declares no
///     <c>ProjectReference</c> to Core at all.
/// </summary>
/// <remarks>
///     <para>
///         One bed pins both halves. Extraction attributes a multiply-declared type to its first
///         declarer, so Tool's reference to its <em>own</em> compiled-in <c>Widget</c> resolves to a node
///         stamped <c>LinkedApp.Core</c>. The survey must not render that as
///         <c>LinkedApp.Tool -&gt; LinkedApp.Core</c>, an edge no project file declares, and must not stay
///         silent about the attribution either, or a reader cannot see whose facts a rule anchored on
///         <c>LinkedApp.Tool</c> answers from.
///     </para>
///     <para>
///         The assertion is the absent edge rather than a count. Tool's only outward type
///         reference is the linked one, so a correct survey renders <em>no</em> Tool→Core edge whatsoever
///         and no ordering accident inside the grouping can satisfy that. <c>LinkedApp.Client</c> is the
///         control beside it: a real <c>ProjectReference</c> and a reference to a type only Core declares,
///         so the suppression is measured against an edge that must survive it.
///     </para>
///     <para>
///         The fixture is read in place rather than leased: nothing here writes into the tree, and the startup
///         sweep has already restored every <c>TestSolutions</c> bed in the output directory.
///         <see cref="GraphCommandTests" /> pins the document's shape against MyApp, and this pins the two
///         things MyApp has no example of.
///     </para>
/// </remarks>
public sealed class LinkedSourceGraphE2ETests
{
    private const string MultiplyDeclaredHeading = "Types declared by more than one project:";

    [Fact]
    public async Task Graph_ProjectSharingASourceFileWithAnother_RendersNoEdgeBetweenThem()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", FixtureBeds.LinkedAppSolution, "--json");

        // Assert — the genuine edge and only the genuine edge. Tool declares no reference to Core and
        // reaches nothing outside itself, so any Tool→Core row here is the phantom.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.ProjectEdges()
            .ShouldBe([("LinkedApp.Client", "LinkedApp.Core", 1)]);
    }

    [Fact]
    public async Task Graph_MultiplyDeclaredType_IsReadableFromTheDocumentAlone()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", FixtureBeds.LinkedAppSolution, "--json");

        // Assert — the whole content of the check-side note, as data: the type, every project declaring it,
        // and which one's facts an arch.Project selection follows. A reader holding this document alone can
        // tell that a subject anchored on LinkedApp.Tool answers from Core's compilation of Widget.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement entry = document.RootElement.GetProperty("multiplyDeclaredTypes")
            .EnumerateArray()
            .ShouldHaveSingleItem();

        entry.GetProperty("type")
            .GetString()
            .ShouldBe("LinkedApp.Shared.Widget");
        entry.Strings("declaredBy")
            .ShouldBe(["LinkedApp.Core", "LinkedApp.Tool"]);
        entry.GetProperty("factsFollow")
            .GetString()
            .ShouldBe("LinkedApp.Core");
    }

    [Fact]
    public async Task Graph_SkeletonGrain_ReportsTheElidedCoverageStatementAsACount()
    {
        // The coverage statement scales with the codebase, so it rides the same rung as the
        // external rows. An elided section must not read as an absent one, which for this key would say the
        // opposite of the truth.

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "graph", FixtureBeds.LinkedAppSolution, "--json", "--grain", "skeleton");

        // Assert
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.TryGetProperty("multiplyDeclaredTypes", out _)
            .ShouldBeFalse();
        document.RootElement.GetProperty("multiplyDeclaredTypeCount")
            .GetInt32()
            .ShouldBe(1);
    }

    [Fact]
    public async Task Graph_ScopedToTheLosingProject_KeepsTheEntryThatExplainsWhatItWillMiss()
    {
        // The scope names LinkedApp.Tool, whose copy of Widget lost the attribution. Keying the
        // narrowing on the winner alone would drop the entry from exactly the scope whose author needs it.

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "graph", FixtureBeds.LinkedAppSolution, "--json", "--projects", "LinkedApp.Tool");

        // Assert — the roster narrows to one project; the entry survives and still names the declarer
        // outside it, which is the half that explains why the type is missing.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.ProjectNames()
            .ShouldBe(["LinkedApp.Tool"]);
        document.RootElement.GetProperty("multiplyDeclaredTypes")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("type")
                .GetString())
            .ShouldBe(["LinkedApp.Shared.Widget"]);
    }

    [Fact]
    public async Task Graph_HumanSurvey_CarriesTheMultiplyDeclaredSectionBetweenTheEdgesAndTheNamespaces()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", FixtureBeds.LinkedAppSolution);

        // Assert — one line per type, naming every declarer and the winner, so the terminal reader gets the
        // same fact the document carries rather than having to reach for --json.
        result.ShouldSucceed();
        result.ShouldHaveOutSection(MultiplyDeclaredHeading)
            .ShouldBe([
                "  LinkedApp.Shared.Widget — declared by LinkedApp.Core, LinkedApp.Tool; "
                + "facts follow LinkedApp.Core"
            ]);
    }

    [Fact]
    public async Task Graph_HumanSurveyAtSkeletonGrain_ElidesTheSectionRatherThanDroppingIt()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", FixtureBeds.LinkedAppSolution, "--grain", "skeleton");

        // Assert — the formatter's standing rule: no section ever disappears, and a coarser grain says what
        // it left out and how to get it back.
        result.ShouldSucceed();
        result.ShouldHaveOutSection(MultiplyDeclaredHeading)
            .ShouldBe([
                "  (elided at skeleton grain — rerun at --grain full for the multiply-declared types)"
            ]);
    }
}
