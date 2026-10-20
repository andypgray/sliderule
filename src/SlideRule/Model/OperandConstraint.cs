namespace SlideRule.Model;

/// <summary>
///     The shared spine of the dependency verbs (GRAMMAR §5.3): the target or source selections a verb names
///     beside its subject are its <see cref="Constraint.Operands" />. A derived verb's domain-named property
///     (<c>Targets</c>, <c>Sources</c>) aliases the one stored list, so every operand walk reaches it through
///     the inherited property.
/// </summary>
internal abstract class OperandConstraint(Selection subject, IReadOnlyList<Selection> operands) : Constraint(subject)
{
    internal sealed override IReadOnlyList<Selection> Operands { get; } = operands;
}
