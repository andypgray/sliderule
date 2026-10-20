using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary><c>.DerivedFrom(typeof(ControllerBase))</c> → " derived from `ControllerBase`" (GRAMMAR §5.2).</summary>
internal sealed class DerivedFromAdjective(TypeAnchor anchor) : SelectionAdjective
{
    internal TypeAnchor Anchor { get; } = anchor;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" derived from {Anchor.BacktickedName()}";
}
