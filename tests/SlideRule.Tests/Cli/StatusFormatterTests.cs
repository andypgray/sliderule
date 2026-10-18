using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Hosting;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     Pins every <c>status</c> line shape (<see cref="StatusFormatter" />) over synthetic
///     <see cref="RuleResult" />s, with no workspace: each posture's row and its skips, the sub-lines that
///     can ride under a row, and the burndown summary.
/// </summary>
public sealed class StatusFormatterTests
{
    // One rule of each posture so the formatter can be pinned over real ArchRules. The five family rules
    // carry the two cell words and both baseline states, which is what decides whether a row gains a
    // sub-line and what that sub-line is keyed by. One of them declares its cells against alphabetical
    // order, because a family whose two orders agree cannot say which of them a sub-line is in, and one is
    // an allow-list, whose dead allowed pattern leaves the rule stricter than written.
    private static readonly ArchitectureModel Model = Checker.Model(arch =>
    {
        Layer checking = arch.Layer("Checking", "App.Checking.*");
        Layer rendering = arch.Layer("Rendering", "App.Rendering.*");
        Layer web = arch.Layer("Web", "App.Web.*");
        Layer api = arch.Layer("Api", "App.Api.*");

        arch.Rule("layering/cuts-not-circular")
            .Migrate("old", arch.Each(checking, rendering).MustNotHaveCircularReferences())
            .Because("b");
        arch.Rule("layering/leaves")
            .Migrate("old", arch.Each(web, api).MustNotReferenceEachOther())
            .Because("b");
        arch.Rule("layering/cuts-independent")
            .Enforce(arch.Each(checking, rendering).MustNotReferenceEachOther())
            .Because("b");
        arch.Rule("modules/independent")
            .Migrate("old", arch.Each(arch.Projects.Matching("App.*")).MustNotReferenceEachOther())
            .Because("b");
        arch.Rule("layering/cuts-only-shared")
            .Migrate("old", arch.Each(checking, rendering).MustOnlyReference(arch.Namespace("App.Shared.*")))
            .Because("b");
        arch.Rule("layering/billing-independent")
            .Enforce(arch.Types.MustHaveSuffix("X"))
            .Because("b");
        arch.Rule("layering/domain-independent")
            .Enforce(arch.Types.MustHaveSuffix("Y"))
            .Because("b");
        arch.Rule("data-access/no-inline-sql")
            .Migrate("old", arch.Types.MustHaveSuffix("Z"))
            .Because("b");
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("App.Legacy.*"))
            .Dragons("d")
            .Because("b");
        arch.Scope("shared/utilities")
            .Caution(arch.Namespace("App.Shared.*"))
            .Dragons("d")
            .Because("b");
    });

    private static string Line(RuleResult result)
    {
        return StatusFormatter.Lines(new CheckReport([result]))
            .First();
    }

    [Fact]
    public void Enforce_Pass_HasNoDetail()
    {
        Line(Result(Model.Rule("layering/billing-independent"), RuleStatus.Passed))
            .ShouldBe("pass layering/billing-independent");
    }

    [Fact]
    public void Enforce_Fail_ShowsViolationCount()
    {
        Line(Result(Model.Rule("layering/domain-independent"), RuleStatus.Failed, 2))
            .ShouldBe("FAIL layering/domain-independent — 2 violations");
    }

    [Fact]
    public void Enforce_PassWithWarning_ShowsWarningCount()
    {
        Line(Result(Model.Rule("layering/billing-independent"), RuleStatus.Passed, warnings: 1))
            .ShouldBe("pass layering/billing-independent — 1 warning");
    }

    [Fact]
    public void QuarantineContainment_Uncaptured_SuggestsInit()
    {
        Line(Result(Model.Rule("legacy/billing/containment"), RuleStatus.Failed, 2))
            .ShouldBe(
                "FAIL legacy/billing/containment (quarantine) — no baseline captured; run 'sliderule baseline init' (2 current violations)");
    }

    [Fact]
    public void QuarantineContainment_Grandfathered_ShowsBurndownAndNeverPromotes()
    {
        string line = Line(Result(Model.Rule("legacy/billing/containment"), RuleStatus.Failed, 1, grandfathered: 2, captured: true));

        line.ShouldBe("FAIL legacy/billing/containment (quarantine) — 2 grandfathered remaining, 1 new, 0 fixed awaiting acceptance");
        line.ShouldNotContain("promotable");
    }

    [Fact]
    public void QuarantineContainment_BurnedToZero_ReadsPlainNotPromotable()
    {
        // Quarantine→Migrate is a human decision; a burned-to-zero containment never suggests promotion.
        string line = Line(Result(Model.Rule("legacy/billing/containment"), RuleStatus.Passed, captured: true));

        line.ShouldBe("pass legacy/billing/containment (quarantine) — 0 grandfathered remaining");
        line.ShouldNotContain("promotable");
    }

    [Fact]
    public void QuarantineTripwire_ReadsDiffAwareSkip()
    {
        Line(Result(Model.Rule("legacy/billing/tripwire"), RuleStatus.Skipped, skipReason: "whatever"))
            .ShouldBe("skip legacy/billing/tripwire (tripwire) — diff-aware; run 'sliderule check --diff-base <ref>'");
    }

    [Fact]
    public void CautionTripwire_ReadsDiffAwareSkip()
    {
        // The same line the quarantine's tripwire prints, and it has to be: what a reader is being told is
        // that the run had no diff to check, which is the same fact under either posture. Without the arm
        // the dispatch falls through to the Enforce line and a rule the run never ran reads "pass".
        Line(Result(Model.Rule("shared/utilities/tripwire"), RuleStatus.Skipped, skipReason: "whatever"))
            .ShouldBe("skip shared/utilities/tripwire (tripwire) — diff-aware; run 'sliderule check --diff-base <ref>'");
    }

    [Fact]
    public void Migrate_NarrowingSkipped_ReadsAsASkipRatherThanAPassWithABurndown()
    {
        // Posture dispatch is what makes this dangerous: without the skip branch a narrowing-skipped Migrate
        // rule falls into the ratchet line and reads "pass … 0 new, 2 fixed awaiting acceptance" — a pass
        // and a burndown for a rule the run never measured.
        string line = Line(Result(
            Model.Rule("data-access/no-inline-sql"), RuleStatus.Skipped, stale: 2, captured: true,
            skipReason: "'BillingOnly.slnf' narrowed this run."));

        line.ShouldBe("skip data-access/no-inline-sql — 'BillingOnly.slnf' narrowed this run.");
        line.ShouldNotContain("awaiting acceptance");
    }

    [Fact]
    public void Enforce_NarrowingSkipped_ReadsAsASkipCarryingItsReason()
    {
        Line(Result(
                Model.Rule("layering/billing-independent"), RuleStatus.Skipped,
                skipReason: "'BillingOnly.slnf' narrowed this run."))
            .ShouldBe("skip layering/billing-independent — 'BillingOnly.slnf' narrowed this run.");
    }

    [Fact]
    public void Migrate_CapturedFailing_ShowsRemainingNewAndAwaiting()
    {
        Line(Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, 1, grandfathered: 1, captured: true))
            .ShouldBe("FAIL data-access/no-inline-sql (migrate) — 1 grandfathered remaining, 1 new, 0 fixed awaiting acceptance");
    }

    [Theory]
    [InlineData(1, "1 grandfathered remaining")]
    [InlineData(3, "1 grandfathered remaining (3 sites)")]
    public void Migrate_GrandfatheredPairCoveringSeveralSites_StatesTheSiteTotal(int sitesEach, string expected)
    {
        // The burndown's real unit: one pair over three sites is three migrations, not one. The parenthetical
        // is self-extinguishing — at one site per pair it says nothing the pair count does not, and the line
        // is byte-for-byte the one this rule printed before the measure existed.
        Line(Result(
                Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, 1, grandfathered: 1, captured: true,
                sitesEach: sitesEach))
            .ShouldBe($"FAIL data-access/no-inline-sql (migrate) — {expected}, 1 new, 0 fixed awaiting acceptance");
    }

    [Fact]
    public void Migrate_ShrunkEntries_TrailTheAwaitingAcceptanceTerm()
    {
        // A matched entry whose pair came in under the count it records: a real reduction, never red, and
        // reported so 'baseline accept-reductions' has something to lower the count to. It trails the stale
        // term rather than joining the head of the list, because like it, it names a state a write clears.
        Line(Result(
                Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 4, captured: true,
                shrunk: 1))
            .ShouldBe(
                "pass data-access/no-inline-sql (migrate) — 4 grandfathered remaining, 0 new, "
                + "0 fixed awaiting acceptance, 1 shrunk");
    }

    [Fact]
    public void Summary_UncountedEntries_CarryTheRecordingNudgeOnceUnderTheBurndown()
    {
        // The nudge is advice, so it rides the summary's uncounted clause and nowhere else: repeated down
        // every rule line it would be noise on a run whose remedy is one command. The rule line still says
        // how many of its own entries are uncounted, which is the number the command will record.
        var report = new CheckReport(
        [
            Result(
                Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 2, captured: true,
                sitesEach: 4, uncounted: 2)
        ]);

        IReadOnlyList<string> lines = StatusFormatter.Lines(report);

        lines[0]
            .ShouldBe(
                "pass data-access/no-inline-sql (migrate) — 2 grandfathered remaining (8 sites), 0 new, "
                + "0 fixed awaiting acceptance, 2 uncounted");
        lines.Last()
            .ShouldBe(
                "Checked 1 rule: 1 passed, 0 failed, 0 skipped. Burndown: 2 grandfathered remaining (8 sites), "
                + "0 fixed awaiting acceptance, 2 uncounted; run 'sliderule baseline accept-reductions' "
                + "to record site counts.");
    }

    [Fact]
    public void Summary_OrphanScan_ListsEachOrphanThenEachUnreadableFileAfterIt()
    {
        var scan = new BaselineOrphanScan(
            [
                new BaselineOrphan("data-access/inline-sql", "arch/baselines/data-access/inline-sql.json", 1),
                new BaselineOrphan("legacy/old", "arch/shared.json", 3)
            ],
            [new UnreadableBaseline("arch/baselines/broken.json", "line 1, column 3: bad token.")]);
        var report = new CheckReport(
            [Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, 2)], scan);

        IReadOnlyList<string> lines = StatusFormatter.Lines(report);

        lines.Skip(1)
            .ShouldBe(
            [
                "Checked 1 rule: 0 passed, 1 failed, 0 skipped. Burndown: 0 grandfathered remaining, 0 fixed "
                + "awaiting acceptance.",
                "orphan: baseline section 'data-access/inline-sql' in arch/baselines/data-access/inline-sql.json "
                + "(1 entry) names no rule in the spec; if a rule was renamed from it, run "
                + "'sliderule baseline rename --from data-access/inline-sql --to <new-id>'",
                "orphan: baseline section 'legacy/old' in arch/shared.json (3 entries) names no rule in the spec; "
                + "if a rule was renamed from it, run 'sliderule baseline rename --from legacy/old --to <new-id>'",
                "unreadable baseline: arch/baselines/broken.json — line 1, column 3: bad token."
            ]);
    }

    [Fact]
    public void Summary_EmptyOrphanScan_PrintsNoOrphanLines()
    {
        RuleResult result = Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, 2);

        IReadOnlyList<string> withEmptyScan = StatusFormatter.Lines(new CheckReport([result], BaselineOrphanScan.Empty));

        // The rule's row and the summary, and nothing after them.
        withEmptyScan.ShouldBe(
        [
            "FAIL data-access/no-inline-sql (migrate) — no baseline captured; run 'sliderule baseline init' "
            + "(2 current violations)",
            "Checked 1 rule: 0 passed, 1 failed, 0 skipped. Burndown: 0 grandfathered remaining, 0 fixed "
            + "awaiting acceptance."
        ]);
    }

    [Fact]
    public void Migrate_PromotableWhenBaselineEmpty()
    {
        Line(Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, captured: true))
            .ShouldBe("pass data-access/no-inline-sql (migrate) — 0 remaining; promotable to Enforce (baseline is empty)");
    }

    [Fact]
    public void Migrate_InterimSuggestsAcceptReductions()
    {
        Line(Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 2, captured: true))
            .ShouldBe(
                "pass data-access/no-inline-sql (migrate) — 0 remaining, 2 fixed awaiting acceptance; " +
                "run 'sliderule baseline accept-reductions'");
    }

    [Fact]
    public void Migrate_Uncaptured_SuggestsInit()
    {
        Line(Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, 2))
            .ShouldBe("FAIL data-access/no-inline-sql (migrate) — no baseline captured; run 'sliderule baseline init' (2 current violations)");
    }

    [Fact]
    public void FamilyMigrate_DebtInSeveralCells_NamesEachOfThemUnderAnUnchangedRow()
    {
        // The burndown's next question after "how much": one cell holding eleven of the twelve pairs is a
        // different morning's work from twelve spread evenly, and the row alone cannot say which this is.
        IReadOnlyList<Violation> debt = [.. SitedDummies(1, 3, "Checking"), .. SitedDummies(11, 8, "Rendering")];

        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport([Result(Model.Rule("layering/cuts-not-circular"), RuleStatus.Passed, captured: true, debt: debt)]));

        // The row is byte-for-byte the row this rule printed before the split existed — the sub-line rides
        // under it, never inside it, which is what keeps the quoted-count gate's anchored pattern matching.
        lines[0]
            .ShouldBe(
                "pass layering/cuts-not-circular (migrate) — 12 grandfathered remaining (91 sites), 0 new, "
                + "0 fixed awaiting acceptance");
        lines[1]
            .ShouldBe("  layers: Checking 1 (3 sites), Rendering 11 (88 sites)");
    }

    [Fact]
    public void FamilyMigrate_DebtInOneCell_KeepsTheKeySingular()
    {
        // The same inflection the summary tails carry: the key counts the cells shown, not the family's.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("layering/cuts-not-circular"), RuleStatus.Passed, captured: true,
                    debt: SitedDummies(3, 3, "Rendering"))
            ]));

        lines[1]
            .ShouldBe("  layer: Rendering 3 (9 sites)");
    }

    [Fact]
    public void FamilyMigrate_OverProjects_KeysOnTheProjectWordAndExtinguishesTheSiteClausePerCell()
    {
        // The site parenthetical is the row's own, reused per cell: a cell whose pairs are one site apiece
        // says nothing the pair count does not, and says it beside a cell that does.
        IReadOnlyList<Violation> debt = [.. SitedDummies(2, 2, "App.Api"), .. SitedDummies(1, 1, "App.Web")];

        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport([Result(Model.Rule("modules/independent"), RuleStatus.Passed, captured: true, debt: debt)]));

        lines[1]
            .ShouldBe("  projects: App.Api 2 (4 sites), App.Web 1");
    }

    [Fact]
    public void FamilyMigrate_CellsDeclaredAgainstAlphabeticalOrder_NamesThemAsTheFamilyDeclaresThem()
    {
        // The sub-line walks the spec's own cell order, which is the order a reader holds the family in —
        // and the only order that stays stable as cells are renamed. Both the debt's arrival order and the
        // names sorted would put Api first here, so this row is what tells the two apart.
        IReadOnlyList<Violation> debt = [.. SitedDummies(1, 2, "Api"), .. SitedDummies(2, 2, "Web")];

        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport([Result(Model.Rule("layering/leaves"), RuleStatus.Passed, captured: true, debt: debt)]));

        lines[1]
            .ShouldBe("  layers: Web 2 (4 sites), Api 1 (2 sites)");
    }

    [Fact]
    public void FamilyMigrate_NothingRemaining_KeepsItsRowToItself()
    {
        // A burned-down family has nowhere to point, and an empty key under the promotion suggestion would
        // be one more line saying nothing.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport([Result(Model.Rule("layering/cuts-not-circular"), RuleStatus.Passed, captured: true)]));

        lines[0]
            .ShouldBe("pass layering/cuts-not-circular (migrate) — 0 remaining; promotable to Enforce (baseline is empty)");
        lines.Count.ShouldBe(2, "the rule's row and the summary, and nothing between them");
    }

    [Fact]
    public void FamilyEnforce_HasNoSubLineHoweverMuchItReds()
    {
        // Keyed on the baseline like every other reader of "is this rule ratcheted": a family with no
        // baseline has no debt to place, only violations to fix, and check is where those are read.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport([Result(Model.Rule("layering/cuts-independent"), RuleStatus.Failed, 4)]));

        lines[0]
            .ShouldBe("FAIL layering/cuts-independent — 4 violations");
        lines.Count.ShouldBe(2);
    }

    [Fact]
    public void NonFamilyMigrate_WithDebt_HasNoSubLine()
    {
        // The other half of the same gate: a plain subject has no cells, so its debt has nowhere to be
        // grouped and its row reads exactly as it always has.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 2, captured: true,
                    sitesEach: 3)
            ]));

        lines[0]
            .ShouldBe(
                "pass data-access/no-inline-sql (migrate) — 2 grandfathered remaining (6 sites), 0 new, "
                + "0 fixed awaiting acceptance");
        lines.Count.ShouldBe(2);
    }

    [Fact]
    public void Migrate_RottedTarget_ReadsNotMeasuredAndWithholdsTheAcceptReductionsNudge()
    {
        // The harm this whole shape exists to prevent. A forbidden target that matched nothing warns and
        // passes with no violations, so every captured entry goes unmatched — which would read as "fixed
        // awaiting acceptance" under a nudge to run the one command that then deletes the section outright.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 12, captured: true,
                    inertHint: "THE-TARGET-CURE")
            ]));

        lines[0]
            .ShouldBe("pass data-access/no-inline-sql (migrate) — 0 remaining, 12 not measured");
        lines[0]
            .ShouldNotContain("accept-reductions");
    }

    [Fact]
    public void Migrate_RottedSubject_ReadsNotMeasuredOnTheThreeCountArmToo()
    {
        // The other rotted shape reaches the other arm: an empty subject IS one new violation, so the rule
        // fails and the row carries all three counters. The term has to be honest in both places, or half the
        // rot keeps reading as paid-down debt.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, captured: true, stale: 12,
                    emptySubjectHint: "THE-SUBJECT-CURE")
            ]));

        lines[0]
            .ShouldBe(
                "FAIL data-access/no-inline-sql (migrate) — 0 grandfathered remaining, 1 new, 12 not measured");
    }

    [Fact]
    public void Migrate_Rotted_CarriesTheSignalsOwnCureOnASubLineUnderTheRow()
    {
        // The cure is the one the signal minted, not a second sentence composed here: status and the check
        // report would otherwise be two places to correct the same advice. The row above keeps its anchored
        // shape — the line rides under it, which is what keeps the quoted-count gate matching.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 2, captured: true,
                    inertHint: "A trailing `.*` covers the namespace itself.")
            ]));

        lines[1]
            .ShouldBe(
                "  not measured: the rule's selection matched nothing, so these entries are untested, not "
                + "fixed. A trailing `.*` covers the namespace itself.");
    }

    [Fact]
    public void Migrate_RottedWithAnEmptyBaseline_IsNotPromotable()
    {
        // Burned to zero and never measured are the same four counts, and only one of them is progress:
        // without the predicate on Promotable this row would invite enforcing a rule that measures nothing.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, captured: true,
                    inertHint: "THE-TARGET-CURE")
            ]));

        lines[0]
            .ShouldBe("pass data-access/no-inline-sql (migrate) — 0 grandfathered remaining");
        lines[0]
            .ShouldNotContain("promotable");
    }

    [Fact]
    public void Migrate_RottedAndUncaptured_StatesTheStateAndWithholdsTheInitNudgeToo()
    {
        // The third arm, and the same reasoning as the acceptance nudge: capturing from a rule that measured
        // nothing signs off "zero debt" nobody saw, which is why the verb refuses it. What is left is the
        // state, which is true whatever the selection did.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed,
                    emptySubjectHint: "THE-SUBJECT-CURE")
            ]));

        lines[0]
            .ShouldBe("FAIL data-access/no-inline-sql (migrate) — no baseline captured (1 current violation)");
        lines[0]
            .ShouldNotContain("baseline init");
        lines[1]
            .ShouldStartWith("  not measured:");
    }

    [Fact]
    public void Enforce_Rotted_KeepsItsRowToItself()
    {
        // The sub-line is keyed on the baseline, like the per-cell split: its sentence is about entries and an
        // Enforce rule has none. Its row is also not the misleading one — it reads the warning count it always
        // read, and check is where the cure for a rule the reader has to fix actually belongs. Two of the
        // violated fixture's rules are exactly this shape, so the gate is exercised on a real report.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("layering/billing-independent"), RuleStatus.Passed, warnings: 1,
                    inertHint: "THE-TARGET-CURE")
            ]));

        lines[0]
            .ShouldBe("pass layering/billing-independent — 2 warnings");
        lines.Count.ShouldBe(2, "the rule's row and the summary, and nothing between them");
    }

    [Fact]
    public void Migrate_PartlyInert_ReadsNotMeasuredAndNamesTheDeadOperandUnderTheRow()
    {
        // The rule measured its live pattern, so the sentence for a whole selection that matched nothing would be
        // false here. One pattern matched no types, and the unmatched entry cannot be read as fixed, since the
        // dead pattern might have been the one to match it.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 2, stale: 1,
                    captured: true, inertOperands: ["the Legacy layer"], operandHint: "THE-OPERAND-CURE")
            ]));

        lines[0]
            .ShouldBe("pass data-access/no-inline-sql (migrate) — 2 grandfathered remaining, 0 new, 1 not measured");
        lines[1]
            .ShouldBe(
                "  not measured: the target operand \"the Legacy layer\" matched no types, so this rule's unmatched "
                + "entries may be untested rather than fixed. THE-OPERAND-CURE");
    }

    [Fact]
    public void Migrate_TwoDeadOperands_NamesBothInOnePluralPhrase()
    {
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 1, captured: true,
                    inertOperands: ["the Legacy layer", "types in `App.Ghost.*`"])
            ]));

        lines[1]
            .ShouldBe(
                "  not measured: the target operands \"the Legacy layer\" and \"types in `App.Ghost.*`\" matched no "
                + "types, so this rule's unmatched entries may be untested rather than fixed.");
    }

    [Fact]
    public void Migrate_PartlyInertAndUncaptured_SaysWhyInitSkipsItInsteadOfSuggestingIt()
    {
        // The verb refuses to capture a rule that measured only part of what it names, so the row cannot
        // suggest it; the sub-line is where a reader learns why, and what clears it.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, 2,
                    inertOperands: ["the Legacy layer"])
            ]));

        lines[0]
            .ShouldBe("FAIL data-access/no-inline-sql (migrate) — no baseline captured (2 current violations)");
        lines[1]
            .ShouldBe(
                "  not measured: the target operand \"the Legacy layer\" matched no types, so 'sliderule baseline "
                + "init' skips this rule until each dead operand matches or moves to a rule of its own.");
    }

    [Fact]
    public void Migrate_PartlyInertWithAnEmptyBaseline_IsNotPromotable()
    {
        // No debt left, no violation and nothing stale would make the rule promotable, but one pattern of its ban
        // matched nothing. The row reads the state and suggests nothing.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, captured: true,
                    inertOperands: ["the Legacy layer"])
            ]));

        lines[0]
            .ShouldBe("pass data-access/no-inline-sql (migrate) — 0 grandfathered remaining");
        lines[0]
            .ShouldNotContain("promotable");
    }

    [Fact]
    public void FamilyMigrate_DeadAllowedPattern_QualifiesTheRowAboveTheCellSplit()
    {
        // The one shape that reaches both sub-lines: a family allow-list rule whose dead allowed pattern
        // leaves it stricter than written while the debt it still tolerates sits in cells. The dead pattern only
        // adds violations, so the unmatched entry is fixed; the sentence naming it still comes first, so the
        // split beneath it is read already qualified.
        IReadOnlyList<Violation> debt = [.. SitedDummies(1, 1, "Checking"), .. SitedDummies(2, 1, "Rendering")];

        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("layering/cuts-only-shared"), RuleStatus.Passed, stale: 1, captured: true,
                    debt: debt, allowedOperands: ["types in `App.Ghost.*`"], operandHint: "THE-OPERAND-CURE")
            ]));

        lines.Take(3)
            .ShouldBe(
            [
                "pass layering/cuts-only-shared (migrate) — 3 grandfathered remaining, 0 new, 1 fixed awaiting acceptance",
                "  stricter than written: the allow-list operand \"types in `App.Ghost.*`\" matched no types. "
                + "THE-OPERAND-CURE",
                "  layers: Checking 1, Rendering 2"
            ]);
        lines.Count.ShouldBe(4, "the rule's row, its two sub-lines and the summary");
    }

    [Fact]
    public void Migrate_DeadAllowedPatternAndUncaptured_SaysWhyInitSkipsItInsteadOfSuggestingIt()
    {
        // A capture would record what the dead pattern was meant to allow as debt, so init refuses the rule and
        // the row cannot suggest it; the sub-line says why, and that the cure is the pattern itself.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("layering/cuts-only-shared"), RuleStatus.Failed, 2,
                    allowedOperands: ["types in `App.Ghost.*`"])
            ]));

        lines[0]
            .ShouldBe("FAIL layering/cuts-only-shared (migrate) — no baseline captured (2 current violations)");
        lines[1]
            .ShouldBe(
                "  stricter than written: the allow-list operand \"types in `App.Ghost.*`\" matched no types, so "
                + "'sliderule baseline init' skips this rule until each dead operand matches or is removed.");
    }

    [Fact]
    public void Migrate_DeadAllowedPatternWithAnEmptyBaseline_IsNotPromotable()
    {
        // No debt left, no violation and nothing stale would make the rule promotable, but a dead allowed pattern
        // leaves it stricter than written. The row reads the state and suggests nothing.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("layering/cuts-only-shared"), RuleStatus.Passed, captured: true,
                    allowedOperands: ["types in `App.Ghost.*`"])
            ]));

        lines[0]
            .ShouldBe("pass layering/cuts-only-shared (migrate) — 0 grandfathered remaining");
    }

    [Fact]
    public void Migrate_DeadAllowedPatternWithNothingRemaining_KeepsTheAcceptReductionsNudge()
    {
        // The dead pattern never unmatched an entry, so the entry is fixed debt and accept-reductions retires it.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("layering/cuts-only-shared"), RuleStatus.Passed, stale: 2, captured: true,
                    allowedOperands: ["types in `App.Ghost.*`"])
            ]));

        lines[0]
            .ShouldBe(
                "pass layering/cuts-only-shared (migrate) — 0 remaining, 2 fixed awaiting acceptance; "
                + "run 'sliderule baseline accept-reductions'");
        lines[1]
            .ShouldBe("  stricter than written: the allow-list operand \"types in `App.Ghost.*`\" matched no types.");
    }

    [Fact]
    public void Summary_RottedAndMeasuredRules_PartitionTheUnmatchedEntriesBetweenTwoTerms()
    {
        // The roll-up does not count rot as fixed debt, and the two terms partition every unmatched entry
        // between them: two were really fixed, twelve were never tested. One total for both would read the
        // rotted rule's whole section as a migration somebody had finished.
        var report = new CheckReport(
        [
            Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 2, captured: true),
            Result(
                Model.Rule("layering/cuts-not-circular"), RuleStatus.Passed, stale: 12, captured: true,
                inertHint: "THE-TARGET-CURE")
        ]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldBe(
                "Checked 2 rules: 2 passed, 0 failed, 0 skipped. Burndown: 0 grandfathered remaining, "
                + "2 fixed awaiting acceptance, 12 not measured.");
    }

    [Fact]
    public void Summary_PartlyInertRule_CountsItsUnmatchedEntriesAsNotMeasured()
    {
        // The same partition, reached by a rule whose selection did match something: its unmatched entries may
        // be fixed or may be untested, so they sit with the rot rather than with the progress.
        var report = new CheckReport(
        [
            Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 2, captured: true),
            Result(
                Model.Rule("layering/cuts-not-circular"), RuleStatus.Passed, stale: 3, captured: true,
                inertOperands: ["the Legacy layer"])
        ]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldBe(
                "Checked 2 rules: 2 passed, 0 failed, 0 skipped. Burndown: 0 grandfathered remaining, "
                + "2 fixed awaiting acceptance, 3 not measured.");
    }

    [Fact]
    public void Migrate_TwoDeadMemberships_NameBothAsStricterThanWritten()
    {
        // A dead membership only adds violations, as a dead allowed pattern does, so its row keeps the fixed term
        // and the sub-line names the memberships by their own noun.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("layering/cuts-only-shared"), RuleStatus.Passed, grandfathered: 1, stale: 1,
                    captured: true, memberships: ["types in `Ghost.*`", "the Legacy layer"])
            ]));

        lines.Take(2)
            .ShouldBe(
            [
                "pass layering/cuts-only-shared (migrate) — 1 grandfathered remaining, 0 new, 1 fixed awaiting acceptance",
                "  stricter than written: the memberships \"types in `Ghost.*`\" and \"the Legacy layer\" matched no types."
            ]);
    }

    [Fact]
    public void Migrate_DeadAmongPartAndUncaptured_ReadsNotMeasuredAndSaysWhyInitSkipsIt()
    {
        // A dead among part can pass a subject as readily as red one, so the rule's entries are in doubt and its
        // cure is to fix or remove the part rather than to move it to a rule of its own.
        IReadOnlyList<string> lines = StatusFormatter.Lines(
            new CheckReport(
            [
                Result(
                    Model.Rule("layering/cuts-only-shared"), RuleStatus.Failed, 2,
                    amongOperands: ["types in `Ghost.*`"])
            ]));

        lines[1]
            .ShouldBe(
                "  not measured: the among operand \"types in `Ghost.*`\" matched no types, so 'sliderule baseline "
                + "init' skips this rule until each dead operand matches or is removed.");
    }

    [Fact]
    public void Summary_DeadAllowedPattern_CountsItsUnmatchedEntriesAsFixed()
    {
        // A dead allowed pattern only adds violations, so the entries a stricter rule left unmatched sit with the
        // progress rather than with the rot.
        var report = new CheckReport(
        [
            Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 2, captured: true),
            Result(
                Model.Rule("layering/cuts-only-shared"), RuleStatus.Passed, stale: 3, captured: true,
                allowedOperands: ["types in `App.Ghost.*`"])
        ]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldBe(
                "Checked 2 rules: 2 passed, 0 failed, 0 skipped. Burndown: 0 grandfathered remaining, "
                + "5 fixed awaiting acceptance.");
    }

    [Fact]
    public void Summary_UncountedOnAStricterRule_KeepsTheRecordingNudge()
    {
        // accept-reductions writes to a rule stricter than written, so the nudge over its entries is advice that
        // works.
        var report = new CheckReport(
        [
            Result(
                Model.Rule("layering/cuts-only-shared"), RuleStatus.Passed, grandfathered: 2, captured: true,
                uncounted: 2, allowedOperands: ["types in `App.Ghost.*`"])
        ]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldEndWith(", 2 uncounted; run 'sliderule baseline accept-reductions' to record site counts.");
    }

    [Fact]
    public void Summary_UncountedOnlyOnAPartlyInertRule_WithholdsTheRecordingNudge()
    {
        // accept-reductions skips a rule not measured in full, so a nudge over that rule's entries alone
        // would name a run that records nothing. The count stays: the entries are uncounted either way.
        var report = new CheckReport(
        [
            Result(
                Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 2, captured: true,
                uncounted: 2, inertOperands: ["the Legacy layer"])
        ]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldBe(
                "Checked 1 rule: 1 passed, 0 failed, 0 skipped. Burndown: 2 grandfathered remaining, "
                + "0 fixed awaiting acceptance, 2 uncounted.");
    }

    [Fact]
    public void Summary_UncountedOnAMeasuredRuleToo_KeepsTheRecordingNudge()
    {
        // The control: once a rule the command will write to holds uncounted entries, the nudge is advice that
        // works, and it counts every uncounted entry the run saw.
        var report = new CheckReport(
        [
            Result(
                Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 2, captured: true,
                uncounted: 2, inertOperands: ["the Legacy layer"]),
            Result(Model.Rule("layering/cuts-not-circular"), RuleStatus.Passed, grandfathered: 1, captured: true, uncounted: 1)
        ]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldEndWith(", 3 uncounted; run 'sliderule baseline accept-reductions' to record site counts.");
    }

    [Fact]
    public void Summary_EveryRuleMeasured_KeepsTheBurndownItAlwaysPrinted()
    {
        // The term extinguishes itself, like the two measure clauses beside it: a run with no rot in it reads
        // byte for byte as it did before rot had a word.
        var report = new CheckReport(
            [Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, stale: 2, captured: true)]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldNotContain("not measured");
    }

    [Fact]
    public void Summary_ReportsRuleCountsAndBurndown()
    {
        var report = new CheckReport(
        [
            Result(Model.Rule("layering/billing-independent"), RuleStatus.Passed),
            Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, grandfathered: 1, captured: true),
            Result(Model.Rule("layering/domain-independent"), RuleStatus.Failed, 1),
            Result(Model.Rule("layering/domain-independent"), RuleStatus.Failed, 1),
            Result(Model.Rule("layering/domain-independent"), RuleStatus.Failed, 1)
        ]);

        StatusFormatter.Lines(report)
            .Last()
            .ShouldBe(
                "Checked 5 rules: 2 passed, 3 failed, 0 skipped. Burndown: 1 grandfathered remaining, 0 fixed awaiting acceptance.");
    }

    private static RuleResult Result(
        ArchRule rule, RuleStatus status, int violations = 0, int warnings = 0, int grandfathered = 0, int stale = 0,
        bool captured = false, string? skipReason = null, int sitesEach = 0, int shrunk = 0, int uncounted = 0,
        IReadOnlyList<Violation>? debt = null, string? inertHint = null, string? emptySubjectHint = null,
        string[]? inertOperands = null, string? operandHint = null, string[]? allowedOperands = null,
        string[]? memberships = null, string[]? amongOperands = null)
    {
        IReadOnlyList<Violation> remaining = debt
                                             ?? (sitesEach > 0
                                                 ? SitedDummies(grandfathered, sitesEach)
                                                 : SyntheticNodes.RuleErrors(grandfathered));

        // The rotted shapes, each arriving the way the checker produces it: an inert target as a warning on a
        // rule that still passes, an empty subject as the one violation that failed it, and each dead operand,
        // whatever its role, as a warning of its own on a rule whose status is its live patterns'. Named after
        // the signal rather than after the row, because which of them a result carries is what decides its arm.
        List<CheckWarning> warned = Enumerable.Range(0, warnings)
            .Select(_ => CheckWarning.InertTarget("w", hint: null))
            .ToList();
        if (inertHint is not null) warned.Add(CheckWarning.InertTarget("inert", inertHint));
        foreach (string operand in inertOperands ?? [])
            warned.Add(CheckWarning.InertTargetOperand(operand, operandHint));
        foreach (string operand in allowedOperands ?? [])
            warned.Add(CheckWarning.DeadAllowedOperand(operand, operandHint));
        foreach (string membership in memberships ?? [])
            warned.Add(CheckWarning.DeadMembership(membership, operandHint));
        foreach (string operand in amongOperands ?? [])
            warned.Add(CheckWarning.DeadAmongOperand(operand, operandHint));

        IReadOnlyList<Violation> red = emptySubjectHint is null
            ? SyntheticNodes.RuleErrors(violations)
            : [Violation.EmptySubject("matched nothing", emptySubjectHint)];

        return new RuleResult(
            rule,
            status,
            red,
            warned,
            skipReason,
            remaining,
            new RatchetMeasure(stale, shrunk, uncounted),
            captured);
    }

    /// <summary>
    ///     The site-carrying sibling: <paramref name="count" /> stand-ins of <paramref name="sites" /> distinct
    ///     <c>file:line</c> sites apiece — the shape a real grandfathered pair has, and the only one that
    ///     reaches the site total. <paramref name="cell" /> labels them as a family verb's per-cell walk
    ///     would, which is what the burndown's sub-line groups by; unlabelled by default, like every
    ///     violation of every rule whose subject is not a family.
    /// </summary>
    /// <remarks>
    ///     Their kind is nothing the burndown reads — it counts pairs and sums sites — so these stay the
    ///     cheapest violation that carries a site, exactly as the ratchet rows' stand-ins do.
    /// </remarks>
    private static IReadOnlyList<Violation> SitedDummies(int count, int sites, string? cell = null)
    {
        return Enumerable.Range(0, count)
            .Select(index => Violation.Shape(
                    SyntheticNodes.Type($"App.Subject{cell}{index}"),
                    SyntheticNodes.Sites($"Subject{cell}{index}.cs", sites))
                .InCell(cell))
            .ToList();
    }
}
