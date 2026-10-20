using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.AttributedWith(typeof(ApiControllerAttribute))</c> → " attributed with `[ApiController]`" (GRAMMAR §5.2).
/// </summary>
internal sealed class AttributedWithAdjective(TypeAnchor anchor) : SelectionAdjective
{
    internal TypeAnchor Anchor { get; } = anchor;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" attributed with {Anchor.BacktickedAttributeName()}";
}
