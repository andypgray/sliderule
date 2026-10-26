namespace SlideRule.Model;

/// <summary>
///     Every selection but a bare layer and a union: a noun plus zero or more adjectives (GRAMMAR §6).
///     A bare noun carries an empty adjective list, and each adjective appends one and re-stamps the owner.
/// </summary>
internal sealed class RefinedSelection : Selection
{
    internal RefinedSelection(Arch owner, SelectionNoun noun)
        : this(owner, noun, Array.Empty<SelectionAdjective>())
    {
    }

    internal RefinedSelection(Arch owner, SelectionNoun noun, IReadOnlyList<SelectionAdjective> adjectives)
        : base(owner)
    {
        Noun = noun;
        Adjectives = adjectives;
    }

    internal override SelectionNoun Noun { get; }

    internal override IReadOnlyList<SelectionAdjective> Adjectives { get; }

    internal override Selection WithRefinements(IReadOnlyList<SelectionAdjective> adjectives)
    {
        return new RefinedSelection(Owner, Noun, adjectives);
    }
}
