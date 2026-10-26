using System.Text.Json;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end <c>graph</c>, <c>check</c> and <c>status</c> against the PolyglotApp fixture — the one
///     solution in the suite holding a project this product cannot read: a real <c>.fsproj</c>, not a
///     renamed <c>.csproj</c>, beside one ordinary C# project.
/// </summary>
/// <remarks>
///     <para>
///         What these pin: a survey that reads only what the C#/Roslyn extractor can must say so about the
///         rest. Otherwise its <c>projects</c> array is shorter than the solution with no count, no list and
///         no note, and a spec derived from it cannot reach whatever ships from the missing projects.
///         <c>check</c> owes the same statement from the other side, or a clean verdict over a polyglot
///         solution never mentions the projects it never measured.
///     </para>
///     <para>
///         The bed uses an <c>.fsproj</c> because F# is the one
///         non-C# kind that reaches a loaded solution at all: it plugs into Roslyn's workspace model while
///         having no Roslyn compiler behind it, so it arrives as a project that supports no compilation.
///         Measured on this bed, it reaches the workspace carrying its output paths, which is why nothing
///         upstream noticed it going missing. The other kinds — <c>.vbproj</c>, <c>.sqlproj</c>,
///         <c>.vcxproj</c>, <c>.shproj</c> — never load at all and are pinned over solution text in
///         <see cref="Roslyn.SolutionProjectFileParserTests" />, where they cost a theory row rather than a
///         second heavyweight bed.
///     </para>
///     <para>
///         The fixture is read in place rather than leased: nothing here writes into the tree, and the startup
///         sweep has already restored every <c>TestSolutions</c> bed in the output directory.
///         <see cref="GraphCommandTests" /> pins the survey document's shape against MyApp, which is all C# and
///         so carries none of these keys.
///     </para>
/// </remarks>
public sealed class PolyglotSurveyE2ETests
{
    private const string UnsupportedHeading = "Projects the solution declares that this survey does not cover:";

    [Theory]
    [InlineData("graph")]
    [InlineData("check")]
    [InlineData("status")]
    public async Task Json_SolutionDeclaringAnFsproj_NamesItAndWhyFromTheDocumentAlone(string verb)
    {
        // The whole content of the coverage statement, as data: which project, and why it is not in the
        // document. A reader holding the survey alone can tell a two-project codebase from a survey that read
        // one project of two. The spec's one rule holds over the C# project, so the check is clean, and that
        // is the point there: no rule can be violated in a project that is not in the model, so only the
        // report can say one is missing. And a project this product cannot read contributes no violations to
        // burn down, so the status document's zero is a property of the tool rather than debt the team paid
        // off.
        string[] arguments = verb == "graph"
            ? [verb, FixtureBeds.PolyglotAppSolution, "--json"]
            : [verb, FixtureBeds.PolyglotAppSolution, "--spec", CliRunner.PolyglotSpecDll, "--json"];

        CliResult result = await CliRunner.InvokeAsync(arguments);

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement entry = document.RootElement.GetProperty("unsupportedProjects")
            .EnumerateArray()
            .ShouldHaveSingleItem();

        entry.GetProperty("project")
            .GetString()
            .ShouldBe("PolyglotApp.Fs/PolyglotApp.Fs.fsproj");
        entry.GetProperty("reason")
            .GetString()
            .ShouldBe("not a C# project");
    }

    [Fact]
    public async Task Graph_SolutionDeclaringAnFsproj_SurveysTheCsharpProjectAndOnlyIt()
    {
        // The control beside the coverage statement. Naming what was not read is only half the
        // fix; the other half is that what WAS read is unchanged, so the key is an addition to a correct
        // survey rather than a repair to a broken one.

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", FixtureBeds.PolyglotAppSolution, "--json");

        // Assert
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.ProjectNames()
            .ShouldBe(["PolyglotApp.Core"]);
    }

    [Fact]
    public async Task Graph_SkeletonGrain_KeepsTheCoverageStatementWhole()
    {
        // Unlike the survey's other two coverage keys this one is NOT elided at any grain: its
        // length scales with the solution rather than the codebase, and the coarser the document the more a
        // reader needs to know it describes part of the solution.

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "graph", FixtureBeds.PolyglotAppSolution, "--json", "--grain", "skeleton");

        // Assert — the entries themselves, not a count standing in for them.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.GetProperty("unsupportedProjects")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("project")
                .GetString())
            .ShouldBe(["PolyglotApp.Fs/PolyglotApp.Fs.fsproj"]);
    }

    [Fact]
    public async Task Graph_ScopedToTheCsharpProject_KeepsTheStatementThatBoundsTheSubject()
    {
        // --projects scopes the survey's roster. The coverage statement is a fact about the load
        // rather than about the roster, so scoping must not silence it: a scoped survey is still not a
        // survey of the whole solution, and its three trust-stamp neighbours do not scope either.

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "graph", FixtureBeds.PolyglotAppSolution, "--json", "--projects", "PolyglotApp.Core");

        // Assert
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.GetProperty("unsupportedProjects")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("project")
                .GetString())
            .ShouldBe(["PolyglotApp.Fs/PolyglotApp.Fs.fsproj"]);
    }

    [Fact]
    public async Task Graph_HumanSurvey_CarriesTheSectionUnderTheRosterItQualifies()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", FixtureBeds.PolyglotAppSolution);

        // Assert — one line per project, naming the reason, so the terminal reader gets the same fact the
        // document carries rather than having to reach for --json.
        result.ShouldSucceed();
        result.ShouldHaveOutSection(UnsupportedHeading)
            .ShouldBe(["  PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project"]);
    }

    [Fact]
    public async Task Check_CleanVerdictOverAPolyglotSolution_DoesNotCallTheModelIncomplete()
    {
        // The posture that separates this key from failedProjects/restoreFailedProjects. A
        // project this product cannot read makes the universe smaller, never wrong, so it must not gate:
        // it takes uncheckedProjects' posture, and the run exits on the verdict alone.

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", FixtureBeds.PolyglotAppSolution, "--spec", CliRunner.PolyglotSpecDll, "--json");

        // Assert — exit 0, and no incompleteness stamp anywhere near it.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.ShouldNotBeStampedIncomplete();
        document.RootElement.TryGetProperty("failedProjects", out _)
            .ShouldBeFalse();
        document.RootElement.TryGetProperty("restoreFailedProjects", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task StatusHumanChannel_OverAPolyglotSolution_StampsTheBurndownItCannotCover()
    {
        // The human twin of the status document's slot, on the verb whose whole output is counts.

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "status", FixtureBeds.PolyglotAppSolution, "--spec", CliRunner.PolyglotSpecDll);

        // Assert — the whole stamp: the counted lede, the evidence line, and the tail that denies the
        // reading a bare zero invites.
        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldStartWith(
                "1 project the solution declares was not surveyed:\n"
                + "  PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project\n"
                + "The burndown below counts only the projects this product can read: these contribute no "
                + "violations and never will, so every count reads low by whatever they hold.");
    }

    [Fact]
    public async Task CheckHumanChannel_OverAPolyglotSolution_StampsTheVerdictItCannotCover()
    {
        // The same stamp on check, whose tail denies the other dangerous reading: that a clean
        // verdict over this solution is a clean solution.

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", FixtureBeds.PolyglotAppSolution, "--spec", CliRunner.PolyglotSpecDll);

        // Assert
        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldStartWith(
                "1 project the solution declares was not surveyed:\n"
                + "  PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project\n"
                + "The verdict below covers only the projects this product can read, so a clean result here "
                + "is not a clean solution.");
    }

    [Fact]
    public async Task CheckSarifChannel_OverAPolyglotSolution_WarnsThatTheScanCoveredPartOfTheSolution()
    {
        // Arrange — the render target with neither an exit code nor a prose channel to fall back on. Code
        // scanning reads the log and nothing else, so a clean SARIF over this solution closes every alert
        // the unreadable project would have raised, with nothing in the document saying it was never
        // scanned. The same silence the two channels above deny, on the surface that cannot be re-read.
        using TempDirectory temp = TestTempRoot.Fresh("polyglot-sarif");
        string sarifPath = temp.PathOf("polyglot.sarif");

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", FixtureBeds.PolyglotAppSolution, "--spec", CliRunner.PolyglotSpecDll, "--sarif", sarifPath);

        // Assert — one notification and no other, so a clean polyglot run is pinned to say exactly this
        // much. The level is read off that notification rather than the log text because it carries the
        // posture its JSON twin pins: a project this product cannot read makes the universe smaller, never
        // wrong, so it warns beside the narrowing notice instead of gating like a failed load.
        result.ShouldSucceed();
        JsonElement notification = File.ReadAllText(sarifPath)
            .SarifNotifications()
            .ShouldHaveSingleItem();

        notification.SarifMessageText()
            .ShouldBe(
                "1 project the solution declares was not surveyed, so these results cover part of the "
                + "solution: PolyglotApp.Fs/PolyglotApp.Fs.fsproj (not a C# project)");
        notification.GetProperty("level")
            .GetString()
            .ShouldBe("warning");
    }
}
