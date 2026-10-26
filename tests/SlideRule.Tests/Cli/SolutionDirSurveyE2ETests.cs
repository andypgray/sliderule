using System.Text.Json;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     Declared project references spelled with <c>$(SolutionDir)</c>, end to end over the
///     <c>SolutionDirApp</c> fixture, whose web and plugin projects reach their dependencies only that way. The
///     property is defined by a solution build and by the workspace load and by nothing else; a project file
///     evaluated without it sees MSBuild's <c>*Undefined*</c> placeholder, which turns each such include into a
///     wildcard that matches no file, so a survey reading the declarations that way reports these projects as
///     declaring no references while their edges still show.
/// </summary>
public sealed class SolutionDirSurveyE2ETests
{
    private const string Core = "SolutionDirApp.Core";
    private const string Web = "SolutionDirApp.Web";
    private const string Plugin = "SolutionDirApp.Plugin";

    [Fact]
    public async Task Graph_ReferencesSpelledWithSolutionDir_AreEachProjectsDeclaredOnes()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", FixtureBeds.SolutionDirAppSolution, "--json");

        // Assert — the plugin declares the web project alone; the core project it also compiles against is the
        // closure's, which the edges show and the declared list does not.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();

        document.SurveyProject(Plugin)
            .Strings("projectReferences")
            .ShouldBe([Web]);
        document.SurveyProject(Web)
            .Strings("projectReferences")
            .ShouldBe([Core]);
        document.SurveyProject(Core)
            .Strings("projectReferences")
            .ShouldBeEmpty();
        document.ProjectEdges()
            .Select(edge => (edge.Source, edge.Target))
            .ShouldBe([(Plugin, Core), (Plugin, Web), (Web, Core)], ignoreOrder: true);
    }

    [Fact]
    public async Task Graph_ReferencesSpelledWithSolutionDir_LeaveNothingUndeclared()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", FixtureBeds.SolutionDirAppSolution, "--json");

        // Assert — every reference the compilations were built against is one a declaration reaches, so no row
        // carries the key that would say otherwise.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();

        document.RootElement.GetProperty("projects")
            .EnumerateArray()
            .Where(project => project.TryGetProperty("undeclaredProjectReferences", out _))
            .Select(project => project.GetProperty("name")
                .GetString())
            .ShouldBeEmpty();
    }
}
