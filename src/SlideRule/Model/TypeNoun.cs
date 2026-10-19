using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     A single type — <c>arch.Type(typeof(SqlConnection))</c>. Bare, it reads as its simple name in both
///     positions, "`SqlConnection`"; the anchor retains the full path so colliding simple names can be
///     qualified in a target list. Under adjectives the phrase switches to the types voice and the name
///     becomes the locative, "public types named `SqlConnection`" (GRAMMAR §5.1, §6).
/// </summary>
internal sealed class TypeNoun(TypeAnchor anchor) : SelectionNoun
{
    /// <summary>The anchor the noun names, carrying every name a checker or a render reads off it.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    // Read only under adjectives: a bare type reads as its reference fragment in both positions.
    internal override string Locative => " named " + ReferenceFragment;

    internal override string ReferenceFragment => Anchor.BacktickedName();

    // The operand names after the one hoisted head, through the shared anchor-list composer, so an
    // adjective on a union of types names every type it narrows: "authored types named `A` or `B`".
    internal override string CollapsedLocative(IReadOnlyList<SelectionNoun> group)
    {
        return " named " + CollapsedReference(group);
    }

    // The shared anchor-list composer, so a union of type nouns disambiguates colliding simple names by
    // exactly the rule every other multi-operand list uses (GRAMMAR §6).
    internal override string CollapsedReference(IReadOnlyList<SelectionNoun> group)
    {
        return ProseFormat.AnchorList(group.Select(noun => ((TypeNoun)noun).Anchor).ToList());
    }
}
