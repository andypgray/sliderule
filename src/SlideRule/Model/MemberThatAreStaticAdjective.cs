namespace SlideRule.Model;

/// <summary>
///     <c>.Fields.ThatAreStatic()</c> premodifies the member head: "static fields of types" (GRAMMAR
///     §5.7, §6) — a member <em>shape</em> adjective, beside the attribute one.
/// </summary>
/// <remarks>
///     A head prefix for the reason <see cref="MemberAttributedWithAdjective" /> gives: the fact is the
///     members' own, so it premodifies the noun it narrows, as <c>static</c> does in a declaration, and the
///     prefix keeps the " of " join, where a trailing "that are static" would switch the subject to
///     " declared in " (<c>SentenceRenderer.MemberPhrase</c>).
///     Member head prefixes <b>concatenate</b> in authoring order (they are an intersection, so the
///     sentence must say both), which is observable here: a shape prefix can stand beside one from
///     another family.
/// </remarks>
internal sealed class MemberThatAreStaticAdjective : MemberAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "static ";
}
