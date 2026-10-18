using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.WithNameMatching("*Handler*")</c> on a member selection premodifies the member head: "`*Handler*`
///     members of types" (GRAMMAR §5.7, §6).
/// </summary>
/// <remarks>
///     A head prefix where the type side's twin is an inline clause, for member <see cref="MemberNamedAdjective" />'s
///     reason: an inline " whose name matches `*Handler*`" after the type reference would read as matching the
///     types' names.
/// </remarks>
internal sealed class MemberWithNameMatchingAdjective(string glob) : MemberAdjective
{
    internal string Glob { get; } = glob;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.Backtick(Glob) + " ";
}
