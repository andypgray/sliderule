using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotSwallow(target, …)</c> → "must not swallow {list}" (GRAMMAR §5.3). Forbids <c>catch</c>
///     clauses whose caught type resolves to a listed target, which carry no <c>when</c> filter, and whose
///     block does not end in a <c>throw</c> — the composite of three facts a handler needs to hold a failure
///     and continue. A bare <c>catch</c> counts as <c>System.Exception</c> and counts as unfiltered; a
///     filtered catch and a rethrowing catch are both lawful.
/// </summary>
internal sealed class MustNotSwallowConstraint(Selection subject, IReadOnlyList<Selection> targets) : OperandConstraint(subject, targets)
{
    internal IReadOnlyList<Selection> Targets => Operands;

    internal override string VerbPhrase => "must not swallow " + Targets.TargetListProse();
}
