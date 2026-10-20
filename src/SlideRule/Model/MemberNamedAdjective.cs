using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.Named("Main")</c> premodifies the member head: "`Main` methods of types in <c>MyApp.*</c>",
///     "`Main` or `Run` methods of types" (GRAMMAR §5.7, §6). The member twin of <see cref="NamedAdjective" />:
///     the exact name, matched ordinally, with no <c>*</c> reading.
/// </summary>
internal sealed class MemberNamedAdjective(IReadOnlyList<string> names) : MemberAdjective
{
    /// <summary>The exact member names, in authoring order.</summary>
    internal IReadOnlyList<string> Names { get; } = names;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.BacktickedList(Names) + " ";
}
