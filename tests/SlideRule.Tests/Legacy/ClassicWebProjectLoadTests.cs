using System.Diagnostics;
using System.Text.Json;
using Shouldly;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Legacy;

/// <summary>
///     A load of the ClassicWeb fixture with Visual Studio's MSBuild selected: a classic ASP.NET Web Application
///     project that imports its web targets through the <c>VSToolsPath</c> default, referenced by an SDK-style
///     project. The web project is evaluated twice, by its own load in the .NET Framework build host and by the
///     SDK-style project's design-time build under the .NET SDK, and both must find Visual Studio's web targets.
/// </summary>
public sealed class ClassicWebProjectLoadTests
{
    private const string WebProject = "Classic.Web";

    private const string Referencer = "Classic.Web.Tests";

    [Fact]
    public async Task Graph_SdkProjectReferencingClassicWebProject_FindsTheWebTargetsInEveryEvaluation()
    {
        CliRunner.SkipUnlessLoadable(CliRunner.ClassicWebSolution);

        // Arrange: a private copy, restored, since the SDK-style project needs its assets file and the bed sits
        // outside the trees the suite restores at startup.
        using var fixture = new TempFixtureWorkspace("LegacySolutions/ClassicWeb", "ClassicWeb.sln");

        // Act: cold, so the load is this run's own rather than the warm pool's earlier one.
        CliResult result = await CliRunner.InvokeColdAsync("graph", fixture.SolutionPath, "--json", "--no-cache");

        // Assert
        string context = result.DescribeLegacyRun();
        result.Exit.ShouldBe(0, context);
        result.Err.ShouldNotContain("Microsoft.WebApplication.targets", customMessage: context);

        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.TryGetProperty("workspaceDiagnostics", out _)
            .ShouldBeFalse(context);
        JsonElement web = document.SurveyProject(WebProject);
        web.Strings("targetFrameworks")
            .ShouldBe(["net48"], context);
        web.GetProperty("types")
            .GetInt32()
            .ShouldBe(1, context);
        document.SurveyProject(Referencer)
            .Strings("projectReferences")
            .ShouldBe([WebProject], context);
        document.ProjectEdges()
            .ShouldBe([(Referencer, WebProject, 1)], context);
    }

    [Fact]
    public async Task Graph_VsToolsPathAlreadyInTheEnvironment_IsTheOneTheEvaluationsUse()
    {
        CliRunner.SkipUnlessLoadable(CliRunner.ClassicWebSolution);

        // Arrange: a fresh process, since this one chose its VSToolsPath when it registered, started with a
        // VSToolsPath naming a directory that holds no targets.
        using var fixture = new TempFixtureWorkspace("LegacySolutions/ClassicWeb", "ClassicWeb.sln");
        using TempDirectory userToolsPath = TestTempRoot.Fresh("vstoolspath");
        ProcessStartInfo startInfo = DotnetCommand.StartInfo(
            [TestsBinCli.Dll(), "graph", fixture.SolutionPath, "--json", "--no-cache"], userToolsPath.Path);
        startInfo.Environment["VSToolsPath"] = userToolsPath.Path;

        // Act
        ChildProcess.ProcessResult result = await ChildProcess.RunAsync(startInfo, TimeSpan.FromMinutes(5), ct: Ct);

        // Assert: the import the .NET SDK's evaluation missed is the one under the user's value.
        string transcript = result.Transcript();
        string missed = Path.Combine(userToolsPath.Path, "WebApplications", "Microsoft.WebApplication.targets");
        result.StandardError.ShouldContain($"The imported project \"{missed}\" was not found.", customMessage: transcript);
    }
}
