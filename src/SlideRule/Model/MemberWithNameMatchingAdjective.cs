using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.WithNameMatching("*Handler*")</c> on a member selection premodifies the member head: "`*Handler*`
///     members of types" (GRAMMAR §5.7, §6).
/// </summary>
internal sealed class MemberWithNameMatchingAdjective(string glob) : MemberAdjective
{
    internal string Glob { get; } = glob;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.Backtick(Glob) + " ";
}
