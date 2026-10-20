using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Implementing(typeof(IHandler&lt;&gt;))</c> → " implementing `IHandler&lt;T&gt;`" (GRAMMAR §5.2).
/// </summary>
internal sealed class ImplementingAdjective(TypeAnchor anchor) : SelectionAdjective
{
    internal TypeAnchor Anchor { get; } = anchor;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" implementing {Anchor.BacktickedName()}";
}
