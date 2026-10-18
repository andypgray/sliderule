using System.Text.Json;
using Shouldly;
using SlideRule.Baselines;
using SlideRule.Roslyn.Baselines;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end <c>baseline</c> against a private, restored copy of the MyApp fixture
///     (<see cref="TempFixtureWorkspace" />, reset between facts): <c>init</c> grandfathers an uncaptured
///     rule's current violations and is idempotent; <c>accept-reductions</c> shrinks a fixed entry and
///     refuses new growth; a hand-edited-up baseline (a forged entry whose seal does not match it) is
///     refused by <c>check</c> and <c>baseline init</c> alike; and a file whose shape its declared
///     version does not describe is refused by <c>check</c> and <c>status</c> without either ever
///     reporting the rule as uncaptured. A section filed under an ID the spec does not declare is listed by
///     <c>status</c> and <c>baseline init</c>, takes the place of <c>check</c>'s init hint, and is moved back
///     by <c>baseline rename</c>. Each fact batches its assertions to keep the expensive CLI/workspace runs to a
///     minimum.
/// </summary>
public sealed class BaselineCommandE2ETests
{
    private const string InlineSqlRule = "data-access/no-inline-sql";

    // The opening words of the hint a rule with nothing captured carries, in both the check report and
    // the status line. Named here because the rows below assert its absence, and an absence pinned
    // against a re-typed fragment stops meaning anything the moment the real sentence is reworded.
    private const string UncapturedHint = "no baseline captured";

    private const string OldId = "data-access/inline-sql";

    private const string OrphanLine =
        "orphan: baseline section 'data-access/inline-sql' in arch/baselines/data-access/inline-sql.json (1 entry) "
        + "names no rule in the spec; if a rule was renamed from it, run "
        + "'sliderule baseline rename --from data-access/inline-sql --to <new-id>'";

    private const string FixedInvoiceController =
        """
        using MyApp.Legacy.Billing;

        namespace MyApp.Web;

        public class InvoiceController
        {
            public decimal Recalculate(decimal amount)
            {
                BillingCalculator calculator = new BillingCalculator();
                return calculator.RoundLineItem(amount, RoundingMode.Bankers);
            }
        }
        """;

    private static readonly string[] ConventionalFile = ["arch", "baselines", "data-access", "no-inline-sql.json"];
    private static readonly string[] OrphanFile = ["arch", "baselines", "data-access", "inline-sql.json"];

    [Fact]
    public async Task BaselineInit_UncapturedRule_CapturesCurrentViolationsAndIsIdempotent()
    {
        using var workspace = new TempFixtureWorkspace();
        string file = workspace.PathOf(ConventionalFile);
        File.Delete(file); // uncaptured rule

        CliResult init = await CliRunner.InvokeAsync(
            "baseline", "init", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);

        init.ShouldSucceed("wrote");
        // init grandfathers the current state: both controllers' DataTable sites.
        File.ReadAllText(file)
            .NormalizedLines()
            .ShouldBe(BothPairsComposed());
        // init also grandfathers the quarantine containment rule into its explicit (uncommitted) baseline —
        // InvoiceController's two interior references into the quarantined billing scope. (The quarantine init e2e.)
        File.ReadAllText(workspace.PathOf("arch", "violated-quarantine-baseline.json"))
            .NormalizedLines()
            .ShouldBe(ContainmentPairsComposed());

        // The survey ends by naming every failing Enforce rule, after the per-file lines, so a first init
        // explains why check will stay red.
        init.Out.ShouldContain("failing with no baseline to capture");
        init.Out.ShouldContain("layering/domain-independent");
        init.Out.ShouldContain("naming/nonexistent");
        init.Out.ShouldContain("exceptions/domain-throws-domain");
        init.Out.ShouldContain("async/accept-cancellation");
        init.Out.ShouldContain("exceptions/no-unfiltered-catch");
        init.Out.ShouldContain("exceptions/no-swallowed-catch");
        init.Out.ShouldContain("exceptions/no-bare-bcl-throw");
        init.Out.IndexOf("wrote", StringComparison.Ordinal)
            .ShouldBeLessThan(init.Out.IndexOf("failing with no baseline to capture", StringComparison.Ordinal));

        // check now sees the Migrate rule fully grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.Out.ShouldContain("\"grandfathered\": 2");

        // A second init is a no-op: the rule is already captured, and the bytes do not change.
        byte[] afterFirst = File.ReadAllBytes(file);
        CliResult init2 = await CliRunner.InvokeAsync(
            "baseline", "init", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        init2.Out.ShouldContain("already captured (2 entries)");
        init2.Out.ShouldContain("unchanged");
        File.ReadAllBytes(file)
            .ShouldBe(afterFirst);
    }

    [Fact]
    public async Task BaselineAcceptReductions_ShrinksAndRefusesGrowth()
    {
        using var workspace = new TempFixtureWorkspace();
        // The checked-in conventional file grandfathers Invoice only. "Fix" Invoice (drop its DataTable);
        // Invoice becomes stale, HomeController is still new — so the ratchet shrinks and refuses growth.
        File.WriteAllText(workspace.PathOf("MyApp.Web", "InvoiceController.cs"), FixedInvoiceController);

        CliResult accept = await CliRunner.InvokeAsync(
            "baseline", "accept-reductions", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);

        accept.ShouldSucceed("accepted 1 reduction");
        accept.Out.ShouldContain("refused 1 addition");
        // The Invoice entry is gone, and with it its seal; the section renders as an empty array.
        File.ReadAllText(workspace.PathOf(ConventionalFile))
            .NormalizedLines()
            .ShouldBe(EmptySectionComposed());

        // The ratchet never gated: HomeController is still red, so check still fails.
        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        check.ShouldReportViolations();
    }

    [Fact]
    public async Task Check_HandEditedUpBaseline_ExitsTwoWithIntegrityError()
    {
        using var workspace = new TempFixtureWorkspace();
        string file = workspace.PathOf(ConventionalFile);
        // Append a HomeController entry by hand — the tamper the ratchet refuses. Forged in the shape of the
        // entry beside it, site count and all, down to a seal that is sixteen lowercase hex characters and
        // simply not this entry's, so what the refusal rests on is the seal rather than anything the walk
        // could notice about the line. HomeController sorts first, so the forgery takes the comma and the
        // captured entry stays last.
        const string forged =
            "        { \"source\": \"T:MyApp.Web.HomeController\", \"target\": \"T:System.Data.DataTable\", " +
            "\"siteCount\": 2, \"seal\": \"0123456789abcdef\" },\n";
        File.WriteAllText(file, File.ReadAllText(file)
            .Replace("      \"entries\": [\n", "      \"entries\": [\n" + forged));

        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        check.ShouldRefuseWith("failed its integrity check");

        // status reads the same baselines, so it refuses identically (it never silently passes tamper).
        CliResult status = await CliRunner.InvokeAsync("status", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        status.ShouldRefuseWith("failed its integrity check");

        // init cannot distinguish tamper from corruption, so it refuses identically.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", "init", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        init.ShouldRefuseWith("failed its integrity check");
    }

    [Fact]
    public async Task Check_BaselineInAShapeItsVersionDoesNotDescribe_RefusesRatherThanReportingItUncaptured()
    {
        using var workspace = new TempFixtureWorkspace();
        string file = workspace.PathOf(ConventionalFile);
        // A whole-file digest beside the entries: the legacy envelope, under a version that describes a
        // sealed file. The digest's own value
        // is never reached — the key is a stranger under this version before anything recomputes it — so
        // sixty-four zeros stand in for a real one, and what the row is about is which of two diagnoses
        // a reader gives a file it cannot read.
        var envelope = $"  \"schemaVersion\": {BaselineFormat.SchemaVersion},\n";
        var digest = $"  \"digest\": \"{new string('0', 64)}\",\n";
        File.WriteAllText(file, File.ReadAllText(file).Replace(envelope, envelope + digest));

        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        check.ShouldRefuseWith("is not valid", "unknown property 'digest'", "no-inline-sql.json");

        // The point of the row, and why the absence is asserted rather than left to the exit code: a file
        // the reader refuses and a rule nothing has captured are different diagnoses whose recoveries run
        // opposite ways. Uncaptured advertises 'baseline init', which grandfathers every current
        // violation fresh and drops the attribution the ratchet exists to keep — the worst available move
        // against a file whose entries are all still there and merely unreadable. So a refusal must never
        // reach the operator wearing the other one's hint, on either channel.
        check.Out.ShouldNotContain(UncapturedHint);
        check.Err.ShouldNotContain(UncapturedHint);

        // status reads the same files through the same store, so it refuses on the same terms.
        CliResult status = await CliRunner.InvokeAsync("status", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        status.ShouldRefuseWith("is not valid", "unknown property 'digest'", "no-inline-sql.json");
        status.Out.ShouldNotContain(UncapturedHint);
        status.Err.ShouldNotContain(UncapturedHint);
    }

    [Fact]
    public async Task BaselineRename_ConstructedOrphan_RestoresTheCommittedFileAndItsGrandfathering()
    {
        using var workspace = new TempFixtureWorkspace();
        string committed = ConstructOrphan(workspace);

        CliResult rename = await CliRunner.InvokeAsync(
            "baseline", "rename", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--from", OldId, "--to", InlineSqlRule);

        rename.ShouldSucceed(
            "data-access/no-inline-sql: renamed from data-access/inline-sql, moving 1 grandfathered entry.",
            "wrote arch/baselines/data-access/no-inline-sql.json",
            "deleted arch/baselines/data-access/inline-sql.json");
        File.ReadAllText(workspace.PathOf(ConventionalFile))
            .NormalizedLines()
            .ShouldBe(committed);
        File.Exists(workspace.PathOf(OrphanFile)).ShouldBeFalse();

        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        string block = check.ShouldHaveRuleBlock(InlineSqlRule);
        block.ShouldContain("  grandfathered: 1 (baselined; run 'sliderule status' for burndown)", check.Transcript());
        block.ShouldNotContain(UncapturedHint, check.Transcript());
    }

    [Fact]
    public async Task Status_ConstructedOrphan_ListsItOnBothChannels()
    {
        using var workspace = new TempFixtureWorkspace();
        ConstructOrphan(workspace);

        CliResult human = await CliRunner.InvokeAsync("status", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        CliResult json = await CliRunner.InvokeAsync(
            "status", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");

        human.Out.NormalizedLines()
            .ShouldContain("\n" + OrphanLine + "\n", human.Transcript());
        using JsonDocument document = json.ShouldHaveJsonStdout();
        JsonElement orphan = document.RootElement.GetProperty("orphans")
            .EnumerateArray()
            .Single();
        orphan.ShouldSatisfyAllConditions(
            () => orphan.GetProperty("ruleId")
                .GetString()
                .ShouldBe(OldId),
            () => orphan.GetProperty("path")
                .GetString()
                .ShouldBe("arch/baselines/data-access/inline-sql.json"),
            () => orphan.GetProperty("entries")
                .GetInt32()
                .ShouldBe(1),
            () => orphan.TryGetProperty("refused", out _)
                .ShouldBeFalse());
    }

    [Fact]
    public async Task BaselineInit_ConstructedOrphan_PrintsTheOrphanLineBeforeCapturing()
    {
        using var workspace = new TempFixtureWorkspace();
        ConstructOrphan(workspace);

        CliResult init = await CliRunner.InvokeAsync(
            "baseline", "init", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);

        // A nudge, not a refusal: init still captures the renamed rule afresh.
        init.ShouldSucceed(OrphanLine, "data-access/no-inline-sql: captured 2 grandfathered violations.");
        init.Out.IndexOf(OrphanLine, StringComparison.Ordinal)
            .ShouldBeLessThan(init.Out.IndexOf("captured", StringComparison.Ordinal), init.Transcript());
    }

    [Fact]
    public async Task Check_ConstructedOrphan_ReplacesTheInitHintWithTheOrphanWarning()
    {
        using var workspace = new TempFixtureWorkspace();
        ConstructOrphan(workspace);

        CliResult human = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        CliResult json = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");

        human.ShouldReportViolations();
        string block = human.ShouldHaveRuleBlock(InlineSqlRule);
        block.ShouldSatisfyAllConditions(
            () => block.ShouldContain(
                "\n  warning: No baseline is captured for this rule, and baseline section 'data-access/inline-sql' in "
                + "arch/baselines/data-access/inline-sql.json (1 entry) names no rule in the spec.\n",
                human.Transcript()),
            () => block.ShouldContain(
                "\n  hint: If this rule was renamed from 'data-access/inline-sql', run 'sliderule baseline rename "
                + "--from data-access/inline-sql --to data-access/no-inline-sql' to keep its entries; if the rule is "
                + "new, run 'sliderule baseline init'.",
                human.Transcript()),
            () => block.ShouldNotContain(UncapturedHint, human.Transcript()));

        json.ShouldReportViolations();
        using JsonDocument document = json.ShouldHaveJsonStdout();
        JsonElement rule = CheckJson.Rule(document, InlineSqlRule);
        rule.GetProperty("warnings")
            .EnumerateArray()
            .Select(warning => warning.GetProperty("kind")
                .GetString())
            .Count(kind => kind == "orphanedBaseline")
            .ShouldBe(1, json.Transcript());
    }

    // Both controllers declare the DataTable twice — as a return type and as a construction, on two lines —
    // so init records two sites under each pair.
    private static string BothPairsComposed()
    {
        return BaselineComposer.Compose(
            InlineSqlRule,
            BaselineEntry.ForEdge("T:MyApp.Web.HomeController", "T:System.Data.DataTable")
                .WithSiteCount(2),
            BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:System.Data.DataTable")
                .WithSiteCount(2));
    }

    private static string EmptySectionComposed()
    {
        return BaselineComposer.Compose(InlineSqlRule);
    }

    // The quarantine containment section captured by init: InvoiceController's two interior references,
    // over three sites between them (the calculator is declared and then constructed; the mode is read once).
    private static string ContainmentPairsComposed()
    {
        return BaselineComposer.Compose(
            "legacy/billing/containment",
            BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:MyApp.Legacy.Billing.BillingCalculator")
                .WithSiteCount(2),
            BaselineEntry.ForEdge("T:MyApp.Web.InvoiceController", "T:MyApp.Legacy.Billing.RoundingMode")
                .WithSiteCount(1));
    }

    // The committed section, re-filed under the old ID and sealed there by the product's own write, and the
    // conventional file deleted. Returns the committed file's text, which a rename back must restore.
    private static string ConstructOrphan(TempFixtureWorkspace workspace)
    {
        string conventional = workspace.PathOf(ConventionalFile);
        string committed = File.ReadAllText(conventional)
            .NormalizedLines();
        BaselineDocument document = BaselineStore.TryReadDocument(conventional)!;
        IReadOnlyList<BaselineEntry> entries = document.Sections[InlineSqlRule];

        IReadOnlyDictionary<string, IReadOnlyList<BaselineEntry>> sections = BaselineComposer.Rules((OldId, [.. entries]));
        BaselineStore.Write(workspace.PathOf(OrphanFile), new BaselineDocument(sections));
        File.Delete(conventional);

        return committed;
    }
}
