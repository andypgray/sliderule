using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.WithNameMatching("*Handler*")</c> on a member selection premodifies the member head: "`*Handler*`
///     members of types" (GRAMMAR §5.7, §6).
/// </summary>
/// <remarks>
///     A head prefix where the type side's twin is an inline clause, for member <see cref="MemberNamedAdjective" />'s
///     reason: the glob is over the members' own names and premodifies them, and the prefix keeps the " of "
///     join, where a trailing " whose name matches `*Handler*`" would switch the subject to " declared in ".
/// </remarks>
internal sealed class MemberWithNameMatchingAdjective(string glob) : MemberAdjective
{
    internal string Glob { get; } = glob;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.Backtick(Glob) + " ";
}
