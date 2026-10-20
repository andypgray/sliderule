using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotThrow(target, …)</c> → "must not throw {list}" (GRAMMAR §5.3), the ban polarity beside the
///     strict allow-list <see cref="MustOnlyThrowConstraint" />.
/// </summary>
internal sealed class MustNotThrowConstraint(Selection subject, IReadOnlyList<Selection> targets) : OperandConstraint(subject, targets)
{
    internal IReadOnlyList<Selection> Targets => Operands;

    internal override string VerbPhrase => "must not throw " + Targets.TargetListProse();
}
