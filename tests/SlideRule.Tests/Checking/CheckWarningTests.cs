using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The orphaned-baseline warning's factory (<see cref="CheckWarning.OrphanedBaseline" />): the pinned message,
///     both forms of its hint, and the structural slots a host reads instead of the prose — the baseline file in
///     <see cref="CheckWarning.File" />, the orphan's rule ID in <see cref="CheckWarning.Key" />. Beside it, the
///     <see cref="CheckWarning.Role" /> each factory stamps.
/// </summary>
public sealed class CheckWarningTests
{
    private static readonly BaselineOrphan OneEntry =
        new("data-access/inline-sql", "arch/baselines/data-access/inline-sql.json", 1);

    [Fact]
    public void OrphanedBaseline_MeasuredInFull_NamesTheSectionAndBothRecoveries()
    {
        CheckWarning warning = CheckWarning.OrphanedBaseline("data-access/no-inline-sql", OneEntry, offerInit: true);

        warning.ShouldSatisfyAllConditions(
            () => warning.Kind.ShouldBe(CheckWarningKind.OrphanedBaseline),
            () => warning.Message.ShouldBe(
                "No baseline is captured for this rule, and baseline section 'data-access/inline-sql' in "
                + "arch/baselines/data-access/inline-sql.json (1 entry) names no rule in the spec."),
            () => warning.Hint.ShouldBe(
                "If this rule was renamed from 'data-access/inline-sql', run 'sliderule baseline rename --from "
                + "data-access/inline-sql --to data-access/no-inline-sql' to keep its entries; if the rule is new, "
                + "run 'sliderule baseline init'."),
            () => warning.File.ShouldBe("arch/baselines/data-access/inline-sql.json"),
            () => warning.Key.ShouldBe("data-access/inline-sql"));
    }

    [Fact]
    public void OrphanedBaseline_NotMeasuredInFull_NamesOnlyTheRename()
    {
        var threeEntries = new BaselineOrphan("legacy/old", "arch/shared.json", 3);

        CheckWarning warning = CheckWarning.OrphanedBaseline("legacy/new", threeEntries, offerInit: false);

        warning.Message.ShouldBe(
            "No baseline is captured for this rule, and baseline section 'legacy/old' in arch/shared.json (3 entries) "
            + "names no rule in the spec.");
        warning.Hint.ShouldBe(
            "If this rule was renamed from 'legacy/old', run 'sliderule baseline rename --from legacy/old --to "
            + "legacy/new' to keep its entries.");
    }

    [Fact]
    public void Factories_EachCarryTheDeadOperandRoleOfTheirPosition()
    {
        // The role is what tells status and baseline which way a dead operand errs, and only the dead-operand
        // factories carry one; every other warning is about something else.
        IReadOnlyList<string> roles =
        [
            $"InertTarget {CheckWarning.InertTarget("m", hint: null).Role}",
            $"DeadOperand(Target) {CheckWarning.DeadOperand(DeadOperandRole.Target, "op", hint: null).Role}",
            $"DeadOperand(AllowList) {CheckWarning.DeadOperand(DeadOperandRole.AllowList, "op", hint: null).Role}",
            $"DeadOperand(Membership) {CheckWarning.DeadOperand(DeadOperandRole.Membership, "op", hint: null).Role}",
            $"DeadOperand(Among) {CheckWarning.DeadOperand(DeadOperandRole.Among, "op", hint: null).Role}",
            $"ScopeTouched {CheckWarning.ScopeTouched(caution: true, "f.cs", "s", "r").Role}",
            $"OrphanedBaseline {CheckWarning.OrphanedBaseline("r", OneEntry, offerInit: true).Role}",
            $"DeadExemplar {CheckWarning.DeadExemplar("ref", hint: null).Role}",
            $"ExemplarFault {CheckWarning.ExemplarFault("ref", "fault").Role}"
        ];

        roles.ShouldBe(
        [
            "InertTarget None",
            "DeadOperand(Target) Target",
            "DeadOperand(AllowList) AllowList",
            "DeadOperand(Membership) Membership",
            "DeadOperand(Among) Among",
            "ScopeTouched None",
            "OrphanedBaseline None",
            "DeadExemplar None",
            "ExemplarFault None"
        ]);
    }

    [Fact]
    public void DeadOperandRoles_OnlyAllowListAndMembershipAreStricter()
    {
        IReadOnlyList<DeadOperandRole> stricter = Enum.GetValues<DeadOperandRole>()
            .Where(role => role.IsStricter())
            .ToList();

        stricter.ShouldBe([DeadOperandRole.AllowList, DeadOperandRole.Membership]);
    }
}
