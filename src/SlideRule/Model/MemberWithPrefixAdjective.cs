using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.WithPrefix("Get")</c> on a member selection premodifies the member head: "`Get*` methods of types"
///     (GRAMMAR §5.7, §6).
/// </summary>
/// <remarks>
///     A head prefix where the type side's twin is an inline clause, for member <see cref="MemberNamedAdjective" />'s
///     reason: the affix is the members' own and premodifies them, and the prefix keeps the " of " join, where a
///     trailing " named `Get*`" would switch the subject to " declared in ".
/// </remarks>
internal sealed class MemberWithPrefixAdjective(string prefix) : MemberAdjective
{
    internal string Prefix { get; } = prefix;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.Backtick(Prefix + "*") + " ";
}
