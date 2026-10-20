namespace SlideRule.Model;

/// <summary>
///     A refinement on a <see cref="Selection" /> — a closed hierarchy so each adjective owns its
///     own prose fragment (GRAMMAR §2 admission rule).
/// </summary>
internal abstract class SelectionAdjective
{
    internal abstract AdjectivePlacement Placement { get; }

    /// <summary>
    ///     The rendered fragment, including its leading separator for inline/subject-final
    ///     placements (e.g. <c> in `MyApp.*`</c>, <c>, except `Foo`</c>); for <see cref="Placement" />
    ///     of <see cref="AdjectivePlacement.Head" /> this is the bare plural that replaces the head.
    /// </summary>
    internal abstract string Fragment { get; }

    /// <summary>
    ///     Whether the fragment opens a parenthetical it does not close — an <c>Except</c> clause — so the
    ///     composer closes it with a comma at whatever junction follows (GRAMMAR §6). Meaningful only for a
    ///     <see cref="AdjectivePlacement.SubjectFinal" /> placement, the one that can end a phrase.
    /// </summary>
    /// <remarks>
    ///     Such a fragment never ends in a comma itself: the same phrase serves subject and reference position,
    ///     so only the junction that follows knows what comes next, and a comma here would land before a period
    ///     or mid-list.
    /// </remarks>
    internal virtual bool OpensParenthetical => false;
}
