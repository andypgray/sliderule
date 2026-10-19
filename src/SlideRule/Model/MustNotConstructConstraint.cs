using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary><c>.MustNotConstruct(target, …)</c> → "must not construct {list}" (GRAMMAR §5.3).</summary>
internal sealed class MustNotConstructConstraint(Selection subject, IReadOnlyList<Selection> targets) : OperandConstraint(subject, targets)
{
    /// <summary>The forbidden construction targets.</summary>
    internal IReadOnlyList<Selection> Targets => Operands;

    internal override string VerbPhrase => "must not construct " + Targets.TargetListProse();
}
