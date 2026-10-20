using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotBeAttributedWith(typeof(TableAttribute), …)</c> → "must not be attributed with
///     `[Table]`" (GRAMMAR §5.3), none-of over the anchor list: a subject violates iff it carries any anchor
///     attribute.
/// </summary>
internal sealed class MustNotBeAttributedWithConstraint(Selection subject, IReadOnlyList<TypeAnchor> anchors) : Constraint(subject)
{
    internal IReadOnlyList<TypeAnchor> Anchors { get; } = anchors;

    internal override string VerbPhrase => "must not be attributed with " + ProseFormat.AttributeList(Anchors);
}
