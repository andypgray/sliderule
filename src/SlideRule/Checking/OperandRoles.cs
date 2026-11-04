using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>One selection operand of a constraint, with the role it plays and the position it resolves in.</summary>
internal sealed class OperandRole(DeadOperandRole role, SelectionPosition position, Selection operand)
{
    internal DeadOperandRole Role { get; } = role;

    internal SelectionPosition Position { get; } = position;

    internal Selection Operand { get; } = operand;
}

/// <summary>
///     The role and resolution position of each selection operand a constraint carries, as the checker reads
///     them: a forbidden set or an allow-list resolves in target position, where a type the solution only
///     references is admitted, and a membership or an among list resolves in subject position.
/// </summary>
/// <remarks>
///     Must agree with <see cref="ConstraintEvaluator" />'s dispatch, which passes the same role and position at
///     each verb's arm. A constraint kind with operands that this table does not name throws, so a new verb
///     cannot go unlisted.
/// </remarks>
internal static class OperandRoles
{
    internal static IReadOnlyList<OperandRole> Of(Constraint constraint)
    {
        IReadOnlyList<Selection> operands = constraint.Operands;
        if (operands.Count == 0) return [];

        (DeadOperandRole role, SelectionPosition position) = RoleOf(constraint);
        return operands.Select(operand => new OperandRole(role, position, operand)).ToList();
    }

    private static (DeadOperandRole, SelectionPosition) RoleOf(Constraint constraint)
    {
        return constraint switch
        {
            MustNotReferenceConstraint or MustNotBeReferencedByConstraint or MustNotConstructConstraint
                or MustNotInjectConstraint or MustNotCatchConstraint or MustNotCatchUnfilteredConstraint
                or MustNotSwallowConstraint or MustNotExposeConstraint or MustNotThrowConstraint =>
                (DeadOperandRole.Target, SelectionPosition.Target),
            MustOnlyReferenceConstraint or MustOnlyBeReferencedByConstraint or MustOnlyThrowConstraint =>
                (DeadOperandRole.AllowList, SelectionPosition.Target),
            MustBelongToConstraint => (DeadOperandRole.Membership, SelectionPosition.Subject),
            MustHaveExactlyOneCounterpartConstraint => (DeadOperandRole.Among, SelectionPosition.Subject),
            _ => throw new InvalidOperationException(
                $"{constraint.GetType().Name} carries selection operands that no operand role names.")
        };
    }
}
