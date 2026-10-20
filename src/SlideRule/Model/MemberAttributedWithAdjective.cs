using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.AttributedWith(typeof(McpServerToolAttribute))</c> premodifies the member head:
///     "`[McpServerTool]`-attributed methods of types in `SlideRule.*`" (GRAMMAR §5.7, §6).
/// </summary>
internal sealed class MemberAttributedWithAdjective(TypeAnchor anchor) : MemberAdjective
{
    internal TypeAnchor Anchor { get; } = anchor;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => Anchor.BacktickedAttributeName() + "-attributed ";
}
