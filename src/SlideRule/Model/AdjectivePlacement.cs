namespace SlideRule.Model;

/// <summary>Where an adjective's fragment lands during subject assembly (GRAMMAR §6).</summary>
internal enum AdjectivePlacement
{
    /// <summary>An inline reduced relative clause, e.g. <c> in `MyApp.*`</c>.</summary>
    Inline,

    /// <summary>Substitutes the subject head plural, e.g. "interfaces".</summary>
    Head,

    /// <summary>
    ///     Premodifies the current subject head rather than replacing it, e.g. "authored types",
    ///     "authored interfaces" — so it composes with a <see cref="Head" /> substitution, and, unlike an inline
    ///     clause, stays on the noun it narrows when the selection renders in reference position. Several stack
    ///     in the order written: "authored public types".
    /// </summary>
    HeadPrefix,

    /// <summary>
    ///     Canonicalized to sentence-final regardless of chain position (<c>Except</c>/<c>Where</c>). Within
    ///     the group an <c>Except</c> renders after every <c>Where</c> of the same subject, so the
    ///     parenthetical it opens is the last thing before the junction that closes it, and no <c>Where</c>
    ///     reads as describing the exception (GRAMMAR §6).
    /// </summary>
    SubjectFinal
}
