using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.AttributedWith(typeof(McpServerToolAttribute))</c> premodifies the member head:
///     "`[McpServerTool]`-attributed methods of types in `SlideRule.*`" (GRAMMAR §5.7, §6). The
///     anchor is a <see cref="TypeAnchor" />, so the string form renders the identical prefix.
/// </summary>
/// <remarks>
///     A head prefix, never the type side's inline clause: <c>arch.Types.AttributedWith(X).Methods</c>
///     renders "methods of types attributed with `[X]`", and the member fact must not read like that for
///     <c>arch.Types.Methods.AttributedWith(X)</c>, a different subject. Prefixing keeps the fact attached
///     to the noun it narrows, as <see cref="AuthoredAdjective" /> does, and keeps the " of " join a trailing
///     clause would switch to " declared in ".
/// </remarks>
internal sealed class MemberAttributedWithAdjective(TypeAnchor anchor) : MemberAdjective
{
    /// <summary>The attribute a selected member carries, by <c>typeof</c> or by definition name.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.Backtick(ProseFormat.AttributeName(Anchor)) + "-attributed ";
}
