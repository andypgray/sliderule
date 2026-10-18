using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.Named("Main")</c> premodifies the member head: "`Main` methods of types in <c>MyApp.*</c>",
///     "`Main` or `Run` methods of types" (GRAMMAR §5.7, §6). The member twin of <see cref="NamedAdjective" />:
///     the exact name, matched ordinally, with no <c>*</c> reading.
/// </summary>
/// <remarks>
///     A head prefix where the type side's twin is an inline clause. A member subject renders its trailing
///     clauses on the kind-plural and then joins its types with " declared in "
///     (<c>SentenceRenderer.MemberPhrase</c>, GRAMMAR §6), so no member fragment can land after the type
///     reference. The names are a prefix because they are the members' own, so they premodify the noun they
///     narrow as the static and attribute prefixes do, and because a prefix keeps the short " of " join:
///     "`Main` methods of types in <c>MyApp.*</c>", where a trailing clause would read "methods named `Main`
///     declared in types in <c>MyApp.*</c>". The names concatenate with the other member prefixes in
///     authoring order.
/// </remarks>
internal sealed class MemberNamedAdjective(IReadOnlyList<string> names) : MemberAdjective
{
    /// <summary>The exact member names, in authoring order.</summary>
    internal IReadOnlyList<string> Names { get; } = names;

    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => ProseFormat.BacktickedList(Names) + " ";
}
