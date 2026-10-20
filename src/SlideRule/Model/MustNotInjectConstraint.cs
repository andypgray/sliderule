using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotInject(target, …)</c> → "must not inject {list}" (GRAMMAR §5.3, §4.7). The
///     captive-dependency verb: a source-level constructor-parameter dependency (primary constructors
///     included).
/// </summary>
internal sealed class MustNotInjectConstraint(Selection subject, IReadOnlyList<Selection> targets) : OperandConstraint(subject, targets)
{
    internal IReadOnlyList<Selection> Targets => Operands;

    internal override string VerbPhrase => "must not inject " + Targets.TargetListProse();
}
