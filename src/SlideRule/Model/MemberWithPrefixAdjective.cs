using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.WithPrefix("Get")</c> on a member selection premodifies the member head: "`Get*` methods of types"
///     (GRAMMAR §5.7, §6).
/// </summary>
internal sealed class MemberWithPrefixAdjective(string prefix) : MemberAdjective
{
    internal string Prefix { get; } = prefix;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.Backtick(Prefix + "*") + " ";
}
