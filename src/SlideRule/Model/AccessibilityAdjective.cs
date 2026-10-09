using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.ThatArePublic()</c>, <c>.ThatAreInternal()</c> and <c>.ThatArePrivate()</c> premodify the
///     subject head: "public types in <c>MyApp.*</c>", "private types" (GRAMMAR §5.2, §6). One node
///     carries the whole accessibility axis, the way <see cref="OfKindAdjective" /> carries the kind
///     axis, so the three spellings are three builder methods over one fact rather than three facts.
/// </summary>
/// <remarks>
///     A head prefix rather than an inline clause, for the reason <see cref="AuthoredAdjective" />
///     gives: a selection can render in reference position, where an inline "that are public" would
///     land after the reference and read as a claim about something else. Prefixing keeps the fact on
///     the noun it narrows, and lets it compose with an <see cref="OfKindAdjective" /> head —
///     "public interfaces in <c>MyApp.*</c>".
/// </remarks>
internal sealed class AccessibilityAdjective(Accessibility accessibility) : SelectionAdjective
{
    internal Accessibility Accessibility { get; } = accessibility;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.AccessibilityPrefix(Accessibility);
}
