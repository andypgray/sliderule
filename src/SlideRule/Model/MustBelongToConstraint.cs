using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustBelongTo(membership, …)</c> → "must belong to {list}" (GRAMMAR §5.3). The coverage verb: a
///     subject type no membership names is red.
/// </summary>
internal sealed class MustBelongToConstraint(Selection subject, IReadOnlyList<Selection> memberships) : OperandConstraint(subject, memberships)
{
    internal IReadOnlyList<Selection> Memberships => Operands;

    internal override string VerbPhrase => "must belong to " + Memberships.TargetListProse();
}
