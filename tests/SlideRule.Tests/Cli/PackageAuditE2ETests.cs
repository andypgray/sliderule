using Xunit;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The grouped NuGet audit line on the stderr of the verbs besides <c>check</c> that render a document:
///     <c>status</c> and <c>graph</c> each echo an advisory once, naming the projects that reported it, with no
///     MSBuild note under it.
/// </summary>
/// <remarks>
///     Driven runner-first with a <see cref="DiagnosticInjectingSolutionSource" /> over the MyApp fixture, which
///     loads cleanly, so the advisory lines are the only diagnostics each run has. Cache-free, because a hit
///     skips the injecting source altogether and a miss would write the injected lines into the cache other
///     classes read.
/// </remarks>
public sealed class PackageAuditE2ETests
{
    private static readonly string[] Projects = ["App", "App.Tests"];

    // Two projects, each reporting the advisory on two frameworks.
    private static readonly IReadOnlyList<string> AdvisoryLines =
        CapturedAuditLines.PerProjectAndFramework(Projects, 2, CapturedAuditLines.BuildTasksGitAdvisory);

    [Fact]
    public async Task Status_AdvisoriesAlone_EchoOneGroupedWarning()
    {
        CliResult result = await RunStatusAsync();

        result.ShouldSucceed();
        result.ShouldEchoOneAuditWarning(CapturedAuditLines.BuildTasksGitAdvisory, Projects);
    }

    [Fact]
    public async Task Graph_AdvisoriesAlone_EchoOneGroupedWarning()
    {
        CliResult result = await RunGraphAsync();

        result.ShouldSucceed();
        result.ShouldEchoOneAuditWarning(CapturedAuditLines.BuildTasksGitAdvisory, Projects);
    }

    private static Task<CliResult> RunStatusAsync()
    {
        string solution = CliRunner.MyAppSolution;
        var source = new DiagnosticInjectingSolutionSource(AdvisoryLines);
        StatusRequest request =
            StatusRequests.For(solution, CliRunner.CleanSpecDll, SolutionPaths.SolutionDirectoryOf(solution))
                with
                {
                    NoCache = true
                };

        return CliResult.CapturedAsync((output, error) =>
            new StatusRunner(output, error, source, new FakeEnvironment()).RunAsync(request, Ct));
    }

    private static Task<CliResult> RunGraphAsync()
    {
        string solution = CliRunner.MyAppSolution;
        var source = new DiagnosticInjectingSolutionSource(AdvisoryLines);
        GraphRequest request = GraphRequests.For(solution, SolutionPaths.SolutionDirectoryOf(solution))
            with
            {
                NoCache = true
            };

        return CliResult.CapturedAsync((output, error) =>
            new GraphRunner(output, error, source, new FakeEnvironment()).RunAsync(request, Ct));
    }
}
