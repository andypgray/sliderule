using System.Globalization;
using System.Text.Json;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end <c>status</c> against the real MyApp fixture solution. Unlike <c>check</c>, status
///     <em>reports</em> — it exits 0 even though the violated spec has red rules — and prints the Migrate
///     burndown (Invoice grandfathered, Home new), including the per-cell split a ratcheted family rule's
///     row carries. The <c>--json</c> document is pinned by a golden.
/// </summary>
public sealed class StatusCommandE2ETests
{
    // The violated-spec burndown, run once for the whole class on each of its two channels. Every fact below
    // asserts a different slice of one report from a byte-identical command line, over paths that are
    // run-stable statics and a fixture tree status never writes to — so re-running per fact would buy five
    // identical extractions and no isolation. The Lazy defers each run until the first fact that needs it.
    private static readonly Lazy<Task<CliResult>> ViolatedHuman = new(() =>
        CliRunner.InvokeAsync("status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll));

    private static readonly Lazy<Task<CliResult>> ViolatedJson = new(() =>
        CliRunner.InvokeAsync("status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json"));

    [Fact]
    public async Task Status_ViolatedSpec_PrintsBurndownAndExitsZero()
    {
        CliResult result = await ViolatedHuman.Value;

        // The Invoice pair covers two DataTable sites, so the burndown states them: the pair count is what
        // the baseline holds and the site count is what the work is. Services-behind-contracts below is one
        // subject declaration, so its line is unchanged — the parenthetical extinguishes itself.
        // The fixture copy sits in this repository's ignored build output, so no commit holds its baselines.
        result.ShouldSucceed(
            "FAIL data-access/no-inline-sql (migrate) — 1 grandfathered remaining (2 sites), 1 new, 0 fixed awaiting acceptance\n"
            + "  oldest entry: age unknown (not committed)",
            // Quarantine containment ratchets like Migrate (uncaptured here) but never suggests promotion.
            "FAIL legacy/billing/containment (quarantine) — no baseline captured; run 'sliderule baseline init' (2 current violations)",
            "skip legacy/billing/tripwire (tripwire) — diff-aware; run 'sliderule check --diff-base <ref>'",
            // A caution's tripwire is the same diff-aware skip, byte for byte: the burndown reads a rule's
            // ratchet, and neither tripwire has one, so the posture that produced the row never shows here.
            "skip domain/retry-budget/tripwire (tripwire) — diff-aware; run 'sliderule check --diff-base <ref>'",
            "FAIL layering/services-behind-contracts (migrate) — 1 grandfathered remaining, 1 new, 0 fixed awaiting acceptance",
            "Checked 32 rules: 3 passed, 27 failed, 2 skipped.",
            "Burndown: 7 grandfathered remaining (12 sites), 0 fixed awaiting acceptance.");
        // The row names the role, never the posture: what the reader is told is that no verdict was reached,
        // not which of the two scope postures declined to reach one.
        result.Out.ShouldNotContain("domain/retry-budget/tripwire (caution)");
    }

    [Fact]
    public async Task Status_FamilyRuleWithABaseline_NamesTheCellsItsDebtSitsIn()
    {
        CliResult result = await ViolatedHuman.Value;

        // The row is the row every ratcheted rule prints; the sub-line under it is the family's addition,
        // keyed by the word one cell is and listing only the cells with something left. MyApp.Domain is a
        // cell of the same family and is absent, because nothing reaches into it.
        result.ShouldSucceed(
            "pass layering/projects-reached-only-by-themselves (migrate) — 5 grandfathered remaining (9 sites), 0 new, 0 fixed awaiting acceptance",
            "  projects: MyApp.Legacy.Billing 3 (5 sites), MyApp.Web 2 (4 sites)");
        result.Out.ShouldNotContain("MyApp.Domain 2");
    }

    [Fact]
    public async Task Status_FamilyRuleWithNoBaseline_KeepsItsRowToItself()
    {
        // The negative control on the same solution: layering/projects-independent is a family over the very
        // same partition and reds every one of the pairs its ratcheted sibling grandfathers, and
        // layering/web-cuts-not-circular is a family of layers. One sub-line in the whole report is what says
        // the split reads tolerated debt rather than violations, and reads it off the baseline rather than
        // off the noun. The age sub-lines ride every rule with remaining entries, so they are not counted.
        CliResult result = await ViolatedHuman.Value;

        result.ShouldSucceed("FAIL layering/projects-independent — 5 violations");
        result.Out.Split('\n')
            .Where(line => line.StartsWith("  ", StringComparison.Ordinal))
            .Where(line => !line.StartsWith("  oldest entry:", StringComparison.Ordinal))
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Status_FamilyRuleJson_SplitsTheRatchetByCellAndLeavesEveryOtherRuleAlone()
    {
        CliResult result = await ViolatedJson.Value;

        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement[] rules = document.RootElement.GetProperty("rules")
            .EnumerateArray()
            .ToArray();

        // One rule in the document carries the key, and every other ratchet block is the block it was before
        // the key existed — including the two other family rules, which carry no baseline.
        string?[] split = rules
            .Where(CarriesCells)
            .Select(rule => rule.GetProperty("id")
                .GetString())
            .ToArray();
        split.ShouldBe(["layering/projects-reached-only-by-themselves"]);

        JsonElement family = rules.Single(CarriesCells)
            .GetProperty("ratchet");
        JsonElement[] cells = family.GetProperty("cells")
            .EnumerateArray()
            .ToArray();

        // The split is a partition of the row above it, not a second measurement: both totals reconcile.
        cells.Sum(cell => cell.GetProperty("remaining")
                .GetInt32())
            .ShouldBe(family.GetProperty("remaining")
                .GetInt32());
        cells.Sum(cell => cell.GetProperty("remainingSites")
                .GetInt32())
            .ShouldBe(family.GetProperty("remainingSites")
                .GetInt32());
    }

    [Fact]
    public async Task Status_ViolatedSpecJson_MatchesGolden()
    {
        CliResult result = await ViolatedJson.Value;

        result.ShouldSucceed();
        result.Out.ShouldMatchGolden("violated-status.json");
    }

    [Fact]
    public async Task Status_CommittedBaselineJson_DatesEachEntryByTheCommitThatAddedIt()
    {
        // Arrange — the fixture committed whole, its baselines included.
        using var repo = new TempGitRepo();
        string committedAt = GitCommand.Output(repo.Root, "log", "-1", "--format=%cI").Trim();

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "status", repo.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");

        // Assert
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement ratchet = document.RootElement.GetProperty("rules")
            .EnumerateArray()
            .Single(rule => rule.GetProperty("id").GetString() == "data-access/no-inline-sql")
            .GetProperty("ratchet");
        ratchet.GetProperty("entries")
            .EnumerateArray()
            .Select(entry => $"{entry.GetProperty("source")} -> {entry.GetProperty("target")}: "
                             + $"{entry.GetProperty("recordedAt").GetDateTimeOffset():O} by {entry.GetProperty("recordedBy")}")
            .ShouldBe(
            [
                "T:MyApp.Web.InvoiceController -> T:System.Data.DataTable: "
                + $"{DateTimeOffset.Parse(committedAt, CultureInfo.InvariantCulture):O} by SlideRule Test"
            ]);
    }

    private static bool CarriesCells(JsonElement rule)
    {
        return rule.TryGetProperty("ratchet", out JsonElement ratchet) && ratchet.TryGetProperty("cells", out _);
    }
}
