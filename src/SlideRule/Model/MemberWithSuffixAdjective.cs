using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.WithSuffix("Async")</c> on a member selection premodifies the member head: "`*Async` methods of types"
///     (GRAMMAR §5.7, §6).
/// </summary>
internal sealed class MemberWithSuffixAdjective(string suffix) : MemberAdjective
{
    internal string Suffix { get; } = suffix;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.SuffixGlob(Suffix) + " ";
}
