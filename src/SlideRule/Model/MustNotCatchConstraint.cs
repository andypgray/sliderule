using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotCatch(target, …)</c> → "must not catch {list}" (GRAMMAR §5.3). Forbids <c>catch</c>
///     clauses whose caught type resolves to a listed target; a bare <c>catch</c> counts as
///     <c>System.Exception</c>.
/// </summary>
internal sealed class MustNotCatchConstraint(Selection subject, IReadOnlyList<Selection> targets) : OperandConstraint(subject, targets)
{
    /// <summary>The forbidden catch targets.</summary>
    internal IReadOnlyList<Selection> Targets => Operands;

    internal override string VerbPhrase => "must not catch " + SentenceRenderer.TargetList(Targets);
}
