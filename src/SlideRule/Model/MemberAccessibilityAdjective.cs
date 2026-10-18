using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Fields.ThatAreProtected()</c> and its five siblings premodify the member head: "protected fields
///     of types", "private protected methods of types" (GRAMMAR §5.7, §6). One node carries the whole
///     accessibility axis, the member twin of <see cref="AccessibilityAdjective" />, so the six spellings are
///     six builder methods over one fact.
/// </summary>
/// <remarks>
///     A head prefix for the reason <see cref="MemberThatAreStaticAdjective" /> gives,
///     concatenating with the other member prefixes in authoring order. The match is exact on the declared
///     accessibility, so <c>ThatAreProtected()</c> leaves the <c>protected internal</c> and
///     <c>private protected</c> members out, and the fragment names the level it matched.
/// </remarks>
internal sealed class MemberAccessibilityAdjective(Accessibility accessibility) : MemberAdjective
{
    internal Accessibility Accessibility { get; } = accessibility;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.AccessibilityPrefix(Accessibility);
}
