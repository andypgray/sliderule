using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Carrying(typeof(AuthorizeAttribute))</c> → " carrying `[Authorize]`" (GRAMMAR §5.2): the attribute
///     declared on the type or inherited from a base type.
/// </summary>
internal sealed class CarryingAdjective(TypeAnchor anchor) : SelectionAdjective
{
    internal TypeAnchor Anchor { get; } = anchor;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" carrying {Anchor.BacktickedAttributeName()}";
}
