using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end acceptance for the derive flow, driven against the golden post-curation
///     artifact (<c>MyAppDerivedSpec</c>) over a private, restored copy of the MyApp fixture
///     (<see cref="TempFixtureWorkspace" />). The one fact walks the whole flow end to end: a derived
///     proposal <c>check</c>s red on a virgin estate, one <c>baseline init</c> grandfathers the debt into
///     conventional baselines, the re-check is green, and <c>render</c> emits the agent context. One fact,
///     because each step reads what the step before it wrote.
/// </summary>
public sealed class DeriveFlowE2ETests
{
    [Fact]
    public async Task GoldenDerivedSpec_VirginEstate_ChecksRedInitsBaselinesThenChecksGreenAndRenders()
    {
        using var workspace = new TempFixtureWorkspace();
        // Virgin estate: drop the fixture's committed baselines so the derived rules meet the code raw.
        string archDir = workspace.PathOf("arch");
        if (Directory.Exists(archDir)) Directory.Delete(archDir, true);

        // Check the derived proposal: red, with the survey's debt surfacing as evidence. 4 rules fail
        // (2 layering-migrate edges, 2 inline-SQL edges, 1 handler-naming shape, 2 containment edges = 7
        // violations across the three Migrate rules and the Quarantine containment); the 2 Enforce directions
        // pass; the Quarantine tripwire skips with no --diff-base.
        CliResult checkRed = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.DerivedSpecDll, "--json");

        checkRed.ShouldReportViolations("\"rulesFailed\": 4");
        checkRed.Out.ShouldContain("\"rulesPassed\": 2");
        checkRed.Out.ShouldContain("\"rulesSkipped\": 1");
        checkRed.Out.ShouldContain("\"violations\": 7");

        // The human baselines the remainder: init grandfathers every current violation into the
        // conventional default path per failing rule (one per Migrate rule + the Quarantine containment).
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", "init", workspace.SolutionPath, "--spec", CliRunner.DerivedSpecDll);

        init.ShouldSucceed();
        // A fully-ratcheted derive leaves nothing unratcheted to warn about — the happy path stays clean.
        init.Out.ShouldNotContain("failing with no baseline to capture");
        File.Exists(workspace.PathOf("arch", "baselines", "layering", "domain-independent.json"))
            .ShouldBeTrue();
        File.Exists(workspace.PathOf("arch", "baselines", "data-access", "no-inline-sql.json"))
            .ShouldBeTrue();
        File.Exists(workspace.PathOf("arch", "baselines", "naming", "handlers.json"))
            .ShouldBeTrue();
        File.Exists(workspace.PathOf("arch", "baselines", "legacy", "billing", "containment.json"))
            .ShouldBeTrue();

        // Re-check: the debt is now the signed baseline, so the run is green with zero active violations.
        CliResult checkGreen = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.DerivedSpecDll, "--json");

        checkGreen.ShouldSucceed("\"rulesFailed\": 0");
        checkGreen.Out.ShouldContain("\"violations\": 0");

        // Render the agent context: the root managed block and the quarantined-scope card for legacy/billing.
        CliResult render = await CliRunner.InvokeAsync(
            "render", workspace.SolutionPath, "--spec", CliRunner.DerivedSpecDll);

        render.ShouldSucceed();
        string rootAgents = workspace.PathOf("AGENTS.md");
        string scopeAgents = workspace.PathOf("MyApp.Legacy.Billing", "AGENTS.md");
        File.Exists(rootAgents)
            .ShouldBeTrue();
        File.ReadAllText(rootAgents)
            .ShouldContain("<!-- sliderule:begin -->");
        File.Exists(scopeAgents)
            .ShouldBeTrue();
        File.ReadAllText(scopeAgents)
            .ShouldContain("## Quarantined scope `legacy/billing`");
    }
}
