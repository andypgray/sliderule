using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.MustNotBeAttributedWith(typeof(ObsoleteAttribute), …)</c> → "must not be attributed
///     with `[Obsolete]`" (GRAMMAR §5.7), the type-side verb phrase reused verbatim. None-of over the anchor
///     list: a subject member violates iff it carries any anchor attribute.
/// </summary>
internal sealed class MemberMustNotBeAttributedWithConstraint(MemberSelection subject, IReadOnlyList<TypeAnchor> anchors)
    : MemberConstraint(subject)
{
    internal IReadOnlyList<TypeAnchor> Anchors { get; } = anchors;

    internal override string VerbPhrase => "must not be attributed with " + ProseFormat.AttributeList(Anchors);
}
