using Shouldly;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Hosting;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     Pins the <c>baseline</c> ratchet survey's verdict lines (<see cref="RatchetSurveyNotice" />) over
///     synthetic <see cref="RuleResult" />s, with no workspace.
/// </summary>
public sealed class RatchetSurveyNoticeTests
{
    // Two Enforce rules (no baseline path) and one Migrate rule (ratcheted), so the formatter's selector
    // can be pinned over real ArchRules.
    private static readonly ArchitectureModel Model = Checker.Model(arch =>
    {
        arch.Rule("layering/billing-independent")
            .Enforce(arch.Types.MustHaveSuffix("X"))
            .Because("b");
        arch.Rule("layering/domain-independent")
            .Enforce(arch.Types.MustHaveSuffix("Y"))
            .Because("b");
        arch.Rule("data-access/no-inline-sql")
            .Migrate("old", arch.Types.MustHaveSuffix("Z"))
            .Because("b");
    });

    [Fact]
    public void NoRatchetedRules_NothingFailing_EmitsNothingToDo()
    {
        var report = new CheckReport(
        [
            Result(Model.Rule("layering/billing-independent"), RuleStatus.Passed),
            Result(Model.Rule("layering/domain-independent"), RuleStatus.Passed)
        ]);

        RatchetSurveyNotice.Lines(report)
            .ShouldBe(
            [
                "no ratcheted rules (Migrate or Quarantine containment) in the spec; nothing to do."
            ]);
    }

    [Fact]
    public void NoRatchetedRules_EnforceFailing_EmitsNothingToCaptureAndNotice()
    {
        var report = new CheckReport(
        [
            Result(Model.Rule("layering/billing-independent"), RuleStatus.Failed, 2),
            Result(Model.Rule("layering/domain-independent"), RuleStatus.Failed, 3)
        ]);

        RatchetSurveyNotice.Lines(report)
            .ShouldBe(
            [
                "no ratcheted rules (Migrate or Quarantine containment) in the spec; nothing to capture.",
                "2 rules are failing with no baseline to capture (5 violations):",
                "  layering/billing-independent — 2 violations",
                "  layering/domain-independent — 3 violations",
                "Enforce carries no baseline, so 'check' stays red on these until each is fixed at the source — " +
                "in the code, in the rule, or by re-posturing the debt as Migrate or Quarantine."
            ]);
    }

    [Fact]
    public void RatchetedRulesPresent_EnforceFailing_EmitsNoticeAlone()
    {
        var report = new CheckReport(
        [
            Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Passed, captured: true),
            Result(Model.Rule("layering/domain-independent"), RuleStatus.Failed, 2)
        ]);

        IReadOnlyList<string> lines = RatchetSurveyNotice.Lines(report);

        lines.ShouldBe(
        [
            "1 rule is failing with no baseline to capture (2 violations):",
            "  layering/domain-independent — 2 violations",
            "Enforce carries no baseline, so 'check' stays red on these until each is fixed at the source — " +
            "in the code, in the rule, or by re-posturing the debt as Migrate or Quarantine."
        ]);
        lines.ShouldNotContain(line => line.Contains("no ratcheted rules", StringComparison.Ordinal));
    }

    [Fact]
    public void RatchetedRulesPresent_OnlyRatchetedFailures_EmitsNothing()
    {
        var report = new CheckReport([Result(Model.Rule("data-access/no-inline-sql"), RuleStatus.Failed, 2)]);

        RatchetSurveyNotice.Lines(report)
            .ShouldBeEmpty();
    }

    [Fact]
    public void SingleRuleSingleViolation_ReadsSingular()
    {
        var report = new CheckReport([Result(Model.Rule("layering/domain-independent"), RuleStatus.Failed, 1)]);

        IReadOnlyList<string> lines = RatchetSurveyNotice.Lines(report);

        lines[1]
            .ShouldBe("1 rule is failing with no baseline to capture (1 violation):");
        lines[2]
            .ShouldBe("  layering/domain-independent — 1 violation");
    }

    private static RuleResult Result(ArchRule rule, RuleStatus status, int violations = 0, bool captured = false)
    {
        return new RuleResult(rule, status, SyntheticNodes.RuleErrors(violations), [], null, [], default, captured);
    }
}
