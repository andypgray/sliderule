using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.WithPrefix("Get")</c> on a member selection premodifies the member head: "`Get*` methods of types"
///     (GRAMMAR §5.7, §6).
/// </summary>
/// <remarks>
///     A head prefix where the type side's twin is an inline clause, for member <see cref="MemberNamedAdjective" />'s
///     reason: an inline " named `Get*`" after the type reference would read as naming the types.
/// </remarks>
internal sealed class MemberWithPrefixAdjective(string prefix) : MemberAdjective
{
    internal string Prefix { get; } = prefix;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.Backtick(Prefix + "*") + " ";
}
