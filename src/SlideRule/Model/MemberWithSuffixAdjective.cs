using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.WithSuffix("Async")</c> on a member selection premodifies the member head: "`*Async` methods of types"
///     (GRAMMAR §5.7, §6).
/// </summary>
/// <remarks>
///     A head prefix where the type side's twin is an inline clause, for member <see cref="MemberNamedAdjective" />'s
///     reason: the affix is the members' own and premodifies them, and the prefix keeps the " of " join, where a
///     trailing " named `*Async`" would switch the subject to " declared in ".
/// </remarks>
internal sealed class MemberWithSuffixAdjective(string suffix) : MemberAdjective
{
    internal string Suffix { get; } = suffix;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.Backtick("*" + Suffix) + " ";
}
