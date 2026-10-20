using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotDeriveFrom(typeof(Exception), …)</c> → "must not derive from `Exception`"
///     (GRAMMAR §5.3), none-of over the anchor list: a subject violates iff it derives from any anchor.
/// </summary>
internal sealed class MustNotDeriveFromConstraint(Selection subject, IReadOnlyList<TypeAnchor> anchors) : Constraint(subject)
{
    internal IReadOnlyList<TypeAnchor> Anchors { get; } = anchors;

    internal override string VerbPhrase => "must not derive from " + ProseFormat.AnchorList(Anchors);
}
