using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.WithSuffix("Async")</c> on a member selection premodifies the member head: "`*Async` methods of types"
///     (GRAMMAR §5.7, §6).
/// </summary>
/// <remarks>
///     A head prefix where the type side's twin is an inline clause, for member <see cref="MemberNamedAdjective" />'s
///     reason: an inline " named `*Async`" after the type reference would read as naming the types.
/// </remarks>
internal sealed class MemberWithSuffixAdjective(string suffix) : MemberAdjective
{
    internal string Suffix { get; } = suffix;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.Backtick("*" + Suffix) + " ";
}
