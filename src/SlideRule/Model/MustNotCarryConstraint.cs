using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotCarry(typeof(SerializableAttribute), …)</c> → "must not carry `[Serializable]`" (GRAMMAR §5.3),
///     none-of over the anchor list: a subject violates iff it declares or inherits any anchor attribute.
/// </summary>
internal sealed class MustNotCarryConstraint(Selection subject, IReadOnlyList<TypeAnchor> anchors) : Constraint(subject)
{
    internal IReadOnlyList<TypeAnchor> Anchors { get; } = anchors;

    internal override string VerbPhrase => "must not carry " + ProseFormat.AttributeList(Anchors);
}
