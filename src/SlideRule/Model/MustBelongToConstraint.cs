using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustBelongTo(membership, …)</c> → "must belong to {list}" (GRAMMAR §5.3). The coverage verb: a
///     subject type no membership names is red. Evaluation is a per-subject membership test rather than
///     an edge walk.
/// </summary>
internal sealed class MustBelongToConstraint(Selection subject, IReadOnlyList<Selection> memberships) : OperandConstraint(subject, memberships)
{
    /// <summary>The memberships the subject must belong to at least one of.</summary>
    internal IReadOnlyList<Selection> Memberships => Operands;

    internal override string VerbPhrase => "must belong to " + SentenceRenderer.TargetList(Memberships);
}
