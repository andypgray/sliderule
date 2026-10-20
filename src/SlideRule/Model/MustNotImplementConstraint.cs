using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotImplement(typeof(IHandler&lt;&gt;), …)</c> → "must not implement `IHandler&lt;T&gt;`"
///     (GRAMMAR §5.3), none-of over the anchor list: a subject violates iff it implements any anchor.
/// </summary>
internal sealed class MustNotImplementConstraint(Selection subject, IReadOnlyList<TypeAnchor> anchors) : Constraint(subject)
{
    internal IReadOnlyList<TypeAnchor> Anchors { get; } = anchors;

    internal override string VerbPhrase => "must not implement " + ProseFormat.AnchorList(Anchors);
}
