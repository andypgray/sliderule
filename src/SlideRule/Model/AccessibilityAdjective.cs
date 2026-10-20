using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.ThatArePublic()</c>, <c>.ThatAreInternal()</c> and <c>.ThatArePrivate()</c> premodify the
///     subject head: "public types in <c>MyApp.*</c>", "private types" (GRAMMAR §5.2, §6).
/// </summary>
internal sealed class AccessibilityAdjective(Accessibility accessibility) : SelectionAdjective
{
    internal Accessibility Accessibility { get; } = accessibility;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.AccessibilityPrefix(Accessibility);
}
