namespace SlideRule.Model;

/// <summary>
///     A refinement on a <see cref="MemberSelection" />, the member twin of <see cref="SelectionAdjective" />
///     (GRAMMAR §4.6). It takes every <see cref="AdjectivePlacement" /> but
///     <see cref="AdjectivePlacement.Head" />: the projection fixes the head, so no member adjective
///     substitutes it (§6).
/// </summary>
/// <remarks>
///     A head prefix keeps the member subject's " of " join, which a trailing clause switches to " declared in "
///     (<c>SentenceRenderer.MemberPhrase</c>), and reads as the members' own fact ("`Main` methods of types")
///     rather than like the type side's clause on a different subject. Head prefixes concatenate in authoring
///     order, an intersection the sentence states whole.
/// </remarks>
internal abstract class MemberAdjective : Adjective;
