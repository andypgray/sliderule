namespace SlideRule.Baselines;

/// <summary>
///     The ratchet's four-way call on one baseline entry: how many sites a run observed under it, against
///     how many it grandfathers.
/// </summary>
/// <remarks>
///     Every path that classifies an entry against a run, the verdict and the baseline command's writes
///     alike, takes this one call so that no two of them can disagree about a state. It returns a
///     decision and renders nothing: each caller words its own sentence for a <see cref="RatchetState" />.
/// </remarks>
internal static class BaselineRatchet
{
    /// <summary>
    ///     Where <paramref name="stored" /> stands against the <paramref name="observedSites" /> distinct
    ///     <c>file:line</c> sites a run saw under its identity.
    /// </summary>
    /// <param name="stored">The entry as the baseline file holds it, not the identity synthesized from the run.</param>
    /// <param name="observedSites">How many sites the run observed; zero where it saw the pair with no evidence.</param>
    /// <remarks>
    ///     <para>
    ///         Two routes reach <see cref="RatchetState.NotMeasured" /> and both mean the same thing — the
    ///         entry grandfathers its pair at pair grain and no count can be recorded for it. A subject
    ///         entry's sites are declarations, so it never carries a measure; counting one as
    ///         <see cref="RatchetState.Uncounted" /> would leave a naming rule's whole section reading
    ///         "uncounted" with nothing an author could do about it. An edge with no sited evidence is the
    ///         same state, arrived at from the run's end.
    ///     </para>
    ///     <para>
    ///         The zero test must stay ahead of the allowance compare. Behind it, an edge the run saw with no
    ///         evidence, against an entry recording sites, reads as <see cref="RatchetState.Shrunk" /> — and
    ///         the write path then lowers the count to zero, which
    ///         <see cref="BaselineEntry.WithSiteCount" /> refuses: an entry grandfathering nothing is a stale
    ///         entry, reported for acceptance rather than recorded.
    ///     </para>
    /// </remarks>
    internal static RatchetState Classify(BaselineEntry stored, int observedSites)
    {
        if (!stored.IsEdge) return RatchetState.NotMeasured;
        if (observedSites == 0) return RatchetState.NotMeasured;
        if (stored.SiteCount is not { } allowance) return RatchetState.Uncounted;
        if (observedSites > allowance) return RatchetState.Grown;

        return observedSites < allowance ? RatchetState.Shrunk : RatchetState.Held;
    }

    /// <summary>
    ///     Where the entry <see cref="RuleBaseline.TryMatch" /> returned for <paramref name="identity" /> stands
    ///     against the <paramref name="observedSites" /> the run saw under it.
    /// </summary>
    /// <param name="identity">The identity the run synthesized for the violation.</param>
    /// <param name="stored">The entry the section matched it to.</param>
    /// <param name="observedSites">How many sites the run observed.</param>
    /// <remarks>
    ///     A project-wide entry standing in for one of the project's items is
    ///     <see cref="RatchetState.Uncounted" />: it grandfathers every item at any number, and a write clears
    ///     it by splitting it into one counted entry per item, as it records a count on an edge that carries
    ///     none.
    /// </remarks>
    internal static RatchetState Classify(BaselineEntry identity, BaselineEntry stored, int observedSites)
    {
        return stored.Equals(identity) ? Classify(stored, observedSites) : RatchetState.Uncounted;
    }
}
