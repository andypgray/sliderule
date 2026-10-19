using Shouldly;
using SlideRule.Cli.Hooks;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Cli;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     End-to-end <c>sliderule hook</c> runs through the real command tree and the real check, with the payload
///     on stdin.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="HookRunnerTests" /> holds the policy with a scripted check. These rows hold the other half:
///         that what the verb hands its exit map is what <c>check</c> really prints and exits over a real solution,
///         and that every failure the CLI would answer with exit 2 comes out of the hook as 1.
///     </para>
///     <para>
///         Each row reads its environment from a fake, with its own state directory, so a test process that
///         itself runs under Claude Code cannot lend a row its real <c>CLAUDE_PROJECT_DIR</c>. A stop that needs no
///         repository fires from a project directory of the row's own, outside every repository, so git discovery
///         cannot lend it the enclosing checkout either.
///     </para>
/// </remarks>
public sealed class HookCommandE2ETests
{
    private const string Category = "hook-verb-e2e";

    private const string QuarantineWarning =
        "Changed file 'MyApp.Legacy.Billing/LegacyNote.cs' is inside quarantined scope 'legacy/billing'";

    [Fact]
    public async Task StopPayload_ViolatedSpec_BlocksWithTheReportOnStderr()
    {
        // Arrange
        using TempDirectory project = TestTempRoot.Fresh(Category);

        // Act
        CliResult result = await HookInProjectAsync(
            project, CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        // Assert: the whole report, rule ID, reason, fix and site, on the channel Claude Code hands back to the
        // agent as its reason to keep working.
        result.ShouldBlockWith(
            "FAIL layering/domain-independent",
            "fix: Define an abstraction in Domain and implement it in Web.",
            "MyApp.Domain/OrderService.cs:9 — MyApp.Domain.OrderService references MyApp.Web.HomeController");
    }

    [Fact]
    public async Task StopPayload_CleanSpec_LetsTheStopStandInSilence()
    {
        // Arrange
        using TempDirectory project = TestTempRoot.Fresh(Category);

        // Act
        CliResult result = await HookInProjectAsync(
            project, CliRunner.MyAppSolution, "--spec", CliRunner.CleanSpecDll);

        // Assert
        result.ShouldProceedSilently();
    }

    [Fact]
    public async Task StopPayload_CleanRunThatWarned_HandsTheAgentTheStopDocument()
    {
        // Arrange: a brand-new untracked file in dragon territory, which the tripwire warns about and no rule reds.
        using var repo = new TempGitRepo();
        repo.WriteQuarantineNote();
        using TempDirectory state = TestTempRoot.Fresh(Category);

        // Act
        CliResult result = await CliRunner.InvokeWithInputAsync(
            HookPayloads.Stop(repo.Root), EnvironmentWith(state.Path, repo.Root),
            "hook", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll);

        // Assert: exit 0 with the one document on stdout, naming the event that fired, because Claude Code reads
        // the context only from a document naming it. The warning came through the diff base the verb defaults to.
        result.Exit.ShouldBe(HookExitCodes.Proceed, result.Transcript());
        string context = result.ShouldHaveHookAdditionalContext("Stop");
        context.ShouldContain("warn legacy/billing/tripwire");
        context.ShouldContain(QuarantineWarning);
    }

    [Fact]
    public async Task EmptyStdin_IsAHandRunThatChecks()
    {
        // Arrange
        using TempDirectory state = TestTempRoot.Fresh(Category);

        // Act: nothing piped in and no project directory, as at a terminal outside Claude Code.
        CliResult result = await CliRunner.InvokeWithInputAsync(
            "", EnvironmentWith(state.Path), "hook", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        // Assert: the per-edit path, which checks, and blocks on a red rule in Claude Code's terms.
        result.ShouldBlockWith("FAIL layering/domain-independent");
    }

    [Fact]
    public async Task MalformedSettingsFile_IsAConfigErrorThatDoesNotBlock()
    {
        // Arrange: the project's settings file, which the hook resolves the solution through when none is named.
        using TempDirectory project = TestTempRoot.Fresh(Category);
        project.WriteFile(["sliderule.json"], "{ \"solution\": ");

        // Act
        CliResult result = await HookInProjectAsync(project);

        // Assert: `check` refuses this file with exit 2, which a hook would read as a red rule and hand the agent
        // to fix. The hook says it under the config-error line and exits 1, for the user to read.
        result.ShouldReportConfigError("sliderule.json: not valid JSON");
    }

    [Fact]
    public async Task MissingSolution_IsAConfigErrorThatDoesNotBlock()
    {
        // Arrange
        using TempDirectory project = TestTempRoot.Fresh(Category);

        // Act: a solution named relative to the session's tree, where there is none.
        CliResult result = await HookInProjectAsync(project, Path.Combine("no-such-directory", "NoSuch.sln"));

        // Assert
        result.ShouldReportConfigError("NoSuch.sln");
    }

    [Fact]
    public async Task NonPositiveMaxRounds_IsAConfigErrorThatDoesNotBlock()
    {
        // Arrange
        using TempDirectory project = TestTempRoot.Fresh(Category);

        // Act
        CliResult result = await HookInProjectAsync(project, "--max-rounds", "0");

        // Assert: a cap of none would stand down before the first block, so it is refused where it is written.
        result.ShouldReportConfigError("--max-rounds must be at least 1.");
    }

    // The state directory is the row's own, and the project directory is whatever the row names or nothing at all,
    // never the real one a test process inherits from a Claude Code session.
    private static FakeEnvironment EnvironmentWith(string stateDirectory, string? projectDirectory = null)
    {
        return new FakeEnvironment()
            .SetVariable(SlideRuleEnvVars.HookStateDirectory, stateDirectory)
            .SetVariable(SlideRuleEnvVars.ClaudeProjectDirectory, projectDirectory);
    }

    // A Stop fired from a project directory of the row's own, which is also the session's cwd and CLAUDE_PROJECT_DIR,
    // with its state inside it: outside every repository, so git discovery cannot hand the row the enclosing checkout.
    private static Task<CliResult> HookInProjectAsync(TempDirectory project, params string[] args)
    {
        string payload = HookPayloads.Stop(project.Path);
        FakeEnvironment environment = EnvironmentWith(project.PathOf("state"), project.Path);

        return CliRunner.InvokeWithInputAsync(payload, environment, ["hook", .. args]);
    }
}
