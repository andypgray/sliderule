using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Fields.ThatAreProtected()</c> and the other member accessibility adjectives premodify the member head:
///     "protected fields of types", "private protected methods of types" (GRAMMAR §5.7, §6). The member twin of
///     <see cref="AccessibilityAdjective" />.
/// </summary>
/// <remarks>
///     The match is exact on the declared accessibility, so <c>ThatAreProtected()</c> leaves the
///     <c>protected internal</c> and <c>private protected</c> members out, and the fragment names the level it
///     matched.
/// </remarks>
internal sealed class MemberAccessibilityAdjective(Accessibility accessibility) : MemberAdjective
{
    internal Accessibility Accessibility { get; } = accessibility;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.AccessibilityPrefix(Accessibility);
}
