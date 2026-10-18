namespace SlideRule.Model;

/// <summary>
///     Every selection but a bare layer and a union: a noun plus zero or more adjectives (GRAMMAR §6).
///     A bare noun carries an empty adjective list, and each adjective appends one and re-stamps the owner.
/// </summary>
internal sealed class RefinedSelection : Selection
{
    internal RefinedSelection(Arch owner, SelectionNoun noun, IReadOnlyList<SelectionAdjective> adjectives)
        : base(owner)
    {
        Noun = noun;
        Adjectives = adjectives;
    }

    internal override SelectionNoun Noun { get; }

    internal override IReadOnlyList<SelectionAdjective> Adjectives { get; }
}
