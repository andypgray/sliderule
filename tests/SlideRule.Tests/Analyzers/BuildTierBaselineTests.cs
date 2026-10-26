using Microsoft.CodeAnalysis;
using SlideRule.Baselines;
using SlideRule.Hosting;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Analyzers;

/// <summary>
///     What the build tier does with a ratcheted rule: check it against the baseline the build handed
///     over, drop it when that file will not read, and — when no baseline reached the build at all —
///     report that the rule went unchecked instead of reporting the debt it was written to grandfather.
/// </summary>
/// <remarks>
///     The three states a ratcheted rule can be in here are the whole subject. Captured, it is checked
///     like any other rule. Refused, it is dropped entirely, the warning in its file having already said
///     why. Uncaptured, it is evaluated only to answer whether it would report at all: a build that
///     printed those findings would print an estate's whole existing debt the first time the analyzer
///     ran, which is the one thing a ratchet exists to prevent.
/// </remarks>
public sealed class BuildTierBaselineTests
{
    private const string UncheckedMessage =
        "Rule 'layering/web-through-the-domain' was not checked in this build. It is ratcheted and the "
        + "build was given no captured baseline for it, so checking it here would report its existing "
        + "violations as new. If its baseline file is kept outside arch/baselines/, add that file to the "
        + "project as a SlideRuleBaseline item; if the rule has no captured baseline yet, 'sliderule "
        + "baseline init' captures one.";

    // A relocated baseline, written the awkward way on purpose: a '..' to fold and a separator the model
    // did not use. Both sides go through one normalizer, so the two spellings meet whichever separator
    // the platform running this treats as one.
    private const string RelocatedPath = "arch/baselines/../relocated\\legacy.json";

    private const string RelocatedFilePath = "/repo/arch/relocated\\legacy.json";

    [Fact]
    public async Task APairTheBaselineHolds_IsSilentWhileAPairItDoesNotIsNewDebt()
    {
        ArchitectureModel model = Checker.Model(BuildTier.MigrateRule);
        string baseline = BuildTier.BaselineHoldingTheStore();

        IReadOnlyList<Diagnostic> reported = await Diagnose(model, (BuildTier.BaselineFilePath, baseline));

        Diagnostic debt = reported.ShouldBeTheOnly("SR2002");
        debt.ShouldSitAt("Web.cs", 7);
        debt.ShouldSay(BuildTier.Composed(model, BuildTier.RuleId, "Data.Ledger"));
    }

    [Fact]
    public async Task AQuarantinesContainmentBroken_IsABrokenLawRatherThanNewDebt()
    {
        // The containment rule ratchets like a Migrate rule, so the baseline carries a section for it —
        // empty, which grandfathers nothing and is a different answer from having no section at all.
        string baseline = BaselineComposer.Compose(("legacy/data/containment", []));

        IReadOnlyList<Diagnostic> reported = await Diagnose(
            Checker.Model(BuildTier.QuarantineScope), (BuildTier.BaselineFilePath, baseline));

        reported.ShouldAllBe("SR2001", 3, "one warning per site the containment is crossed at");
    }

    [Fact]
    public async Task APairThatHasGrownPastItsBaseline_IsNewDebtAtEverySiteWithBothCounts()
    {
        ArchitectureModel model = Checker.Model(BuildTier.MigrateRule);
        string baseline = BaselineComposer.Compose(
            BuildTier.RuleId,
            BaselineEntry.ForEdge(BuildTier.OwnTypeId, BuildTier.StoreId).WithSiteCount(1),
            BaselineEntry.ForEdge(BuildTier.OwnTypeId, BuildTier.LedgerId).WithSiteCount(1));

        IReadOnlyList<Diagnostic> reported = await Diagnose(model, (BuildTier.BaselineFilePath, baseline));

        IReadOnlyList<Diagnostic> grown = reported.ShouldAllBe(
            "SR2002", 2, "one warning per site of the pair that grew");
        string expected = BuildTier.Composed(
            model, BuildTier.RuleId, "Data.Store",
            "The baseline holds 1 site of this pair and 2 were found.");
        grown[0]
            .ShouldSitAt("Web.cs", 5);
        grown[0]
            .ShouldSay(expected);
        grown[1]
            .ShouldSitAt("Web.cs", 6);
        grown[1]
            .ShouldSay(expected);
    }

    /// <summary>Each way a ratcheted rule's baseline can fail to reach the build captured.</summary>
    public static TheoryData<string> Uncaptured =>
    [
        "no baseline file handed over",
        "a baseline file with no section for the rule",
        "a baseline file that will not hand over its text"
    ];

    [Theory]
    [MemberData(nameof(Uncaptured))]
    public async Task ARatchetedRuleWithNoCapturedBaseline_NamesItselfUncheckedAndReportsNoViolation(
        string arrangement)
    {
        IReadOnlyList<Diagnostic> reported = await Diagnose(
            Checker.Model(BuildTier.MigrateRule), BaselinesFor(arrangement));

        reported.ShouldBeTheOnly("SR2092")
            .ShouldSay(UncheckedMessage);
    }

    [Fact]
    public async Task AnUncapturedRuleThatWouldReportNothingHere_SaysNothingEither()
    {
        // The warning is about a rule that would have reported, not about every ratcheted rule in the
        // model: a project the debt never reached hears nothing.
        IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(
            BuildTier.OwnCompilation(source: BuildTier.CleanSource),
            [BuildTier.ModelFile(BuildTier.ModelText(BuildTier.MigrateRule))],
            cancellationToken: Ct);

        reported.ShouldBeSilent();
    }

    [Fact]
    public async Task ABaselineEntryThatFailsItsSeal_IsOneWarningInTheBaselineFileAndTheRuleIsDropped()
    {
        string composed = BuildTier.BaselineHoldingTheStore();
        string tampered = composed.Replace(BuildTier.StoreId, "T:Data.Cellar");

        IReadOnlyList<Diagnostic> reported = await Diagnose(
            Checker.Model(BuildTier.MigrateRule), (BuildTier.BaselineFilePath, tampered));

        Diagnostic refused = reported.ShouldBeTheOnly("SR2091");
        refused.ShouldSay(
            "This baseline file could not be read, so this build did not check the rules that use it: the "
            + "entry 'T:Web.OrderController -> T:Data.Cellar' under rule "
            + "'layering/web-through-the-domain' does not match its seal. 'sliderule check' reports the "
            + "same refusal with its recovery steps.");
        refused.ShouldSitAt(BuildTier.BaselineFilePath, 6, 9);
    }

    [Fact]
    public async Task ABaselineFileThatIsNotJson_IsOneWarningInItAtTheParsersOwnPosition()
    {
        IReadOnlyList<Diagnostic> reported = await Diagnose(
            Checker.Model(BuildTier.MigrateRule), (BuildTier.BaselineFilePath, "{ this is not json"));

        Diagnostic refused = reported.ShouldBeTheOnly("SR2091");
        refused.ShouldSay(
            "This baseline file could not be read, so this build did not check the rules that use it: "
            + "expected a key or '}', found 't'. 'sliderule check' reports the same refusal with its "
            + "recovery steps.");
        refused.ShouldSitAt(BuildTier.BaselineFilePath, 1, 3);
    }

    [Fact]
    public async Task ASecondRuleWithAReadableBaseline_ReportsNormallyBesideARefusedOne()
    {
        ArchitectureModel model = Checker.Model(TwoRatchetedRules);
        string refused = BuildTier.BaselineHoldingTheStore()
            .Replace(BuildTier.StoreId, "T:Data.Cellar");
        string readable = BaselineComposer.Compose(
            BuildTier.SecondRuleId, BaselineEntry.ForEdge(BuildTier.OwnTypeId, BuildTier.StoreId).WithSiteCount(2));

        IReadOnlyList<Diagnostic> reported = await Diagnose(
            model, (BuildTier.BaselineFilePath, refused), (BuildTier.OtherBaselineFilePath, readable));

        reported.ShouldHaveSingle("SR2091");
        // The second rule's one pair its own baseline does not hold.
        reported.ShouldHaveSingle("SR2002")
            .ShouldSay(BuildTier.Composed(model, BuildTier.SecondRuleId, "Data.Ledger"));
        reported.ShouldNotReport("SR2092", "a rule dropped for a refused baseline is not also reported as unchecked");
    }

    [Fact]
    public async Task ABaselineRelocatedOutOfTheConventionalDirectory_IsFoundWhereTheResolverSaysItIs()
    {
        ArchitectureModel model = Checker.Model(arch => BuildTier.MigrateRule(arch, BuildTier.RuleId, RelocatedPath));
        string baseline = BaselineComposer.Compose(
            BuildTier.RuleId,
            BaselineEntry.ForEdge(BuildTier.OwnTypeId, BuildTier.StoreId).WithSiteCount(2),
            BaselineEntry.ForEdge(BuildTier.OwnTypeId, BuildTier.LedgerId).WithSiteCount(1));

        IReadOnlyList<Diagnostic> found = await Diagnose(model, (RelocatedFilePath, baseline));
        IReadOnlyList<Diagnostic> elsewhere = await Diagnose(model, ("/repo/arch/baselines/legacy.json", baseline));

        // The legs must differ, or a run that found nothing would read as a run that grandfathered
        // everything: handed at the resolved path the whole debt is held, handed anywhere else the rule
        // is uncaptured and says so.
        found.ShouldBeSilent();
        elsewhere.ShouldBeTheOnly("SR2092");
    }

    private static void TwoRatchetedRules(Arch arch)
    {
        Layer web = arch.Layer("Web", "Web.*");
        Layer data = arch.Layer("Data", "Data.*");
        BuildTier.MigrateRule(arch, web, data, BuildTier.RuleId, BuildTier.BaselinePath);
        BuildTier.MigrateRule(arch, web, data, BuildTier.SecondRuleId, BuildTier.OtherBaselinePath);
    }

    private static Task<IReadOnlyList<Diagnostic>> Diagnose(
        ArchitectureModel model, params (string Path, string? Text)[] baselines)
    {
        List<AdditionalText> files =
        [
            BuildTier.ModelFile(BuildTier.ModelTextFor(model)),
            .. baselines.Select(baseline => BuildTier.File(baseline.Path, baseline.Text))
        ];

        return BuildTier.DiagnoseAsync(BuildTier.OwnCompilation(), files, cancellationToken: Ct);
    }

    private static (string Path, string? Text)[] BaselinesFor(string arrangement)
    {
        return arrangement switch
        {
            "no baseline file handed over" => [],
            "a baseline file with no section for the rule" =>
            [
                (BuildTier.BaselineFilePath, BaselineComposer.Compose(
                    "some/other-rule",
                    BaselineEntry.ForEdge(BuildTier.OwnTypeId, BuildTier.StoreId).WithSiteCount(1)))
            ],
            "a baseline file that will not hand over its text" => [(BuildTier.BaselineFilePath, null)],
            _ => throw new ArgumentOutOfRangeException(nameof(arrangement), arrangement, null)
        };
    }
}
