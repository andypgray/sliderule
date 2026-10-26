namespace SlideRule.Model;

/// <summary>The shape every adjective stratum shares.</summary>
internal abstract class Adjective
{
    internal abstract AdjectivePlacement Placement { get; }

    /// <summary>
    ///     The rendered fragment, including its leading separator for inline/subject-final
    ///     placements (e.g. <c> in `MyApp.*`</c>, <c>, except `Foo`</c>); for <see cref="Placement" />
    ///     of <see cref="AdjectivePlacement.Head" /> this is the text that replaces the head: the bare plural for
    ///     a type adjective, the whole phrase for a project adjective.
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
