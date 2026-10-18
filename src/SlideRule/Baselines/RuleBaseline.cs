using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>
///     One rule's captured baseline: the set of <see cref="BaselineEntry" /> identities whose
///     violations are grandfathered — reported, but not failing the check. A section with zero entries
///     is still a captured baseline; a rule with no captured baseline at all is absent from its
///     <see cref="BaselineIndex" /> rather than present and empty.
/// </summary>
internal sealed class RuleBaseline
{
    private readonly Dictionary<BaselineEntry, BaselineEntry> _lookup;

    /// <summary>
    ///     Builds a section from the entries read out of a baseline file. Their order does not matter, and
    ///     entries sharing an identity are collapsed into one: the first of them keeps its reason and its
    ///     site count.
    /// </summary>
    internal RuleBaseline(IReadOnlyCollection<BaselineEntry> entries)
    {
        Guard.NotNull(entries, nameof(entries));
        _lookup = new Dictionary<BaselineEntry, BaselineEntry>();

        // First entry of a given identity wins, so a later duplicate never overwrites an earlier one's
        // attribution.
        foreach (BaselineEntry entry in entries)
            if (!_lookup.ContainsKey(entry))
                _lookup.Add(entry, entry);

        Entries = BaselineEntry.InCanonicalOrder(_lookup.Values);
    }

    /// <summary>
    ///     Gets the grandfathered entries in the baseline file's own order (by source or subject symbol
    ///     ID, then by target, ordinal), with entries sharing an identity collapsed into one.
    /// </summary>
    public IReadOnlyList<BaselineEntry> Entries { get; }

    /// <summary>Gets how many entries this section grandfathers.</summary>
    public int Count => Entries.Count;

    /// <summary>
    ///     Looks up the stored entry whose identity matches <paramref name="identity" />. Pass the entry
    ///     <c>Violation.BaselineIdentity()</c> returns: an identity ignores the reason and the site count,
    ///     so what comes back is the entry as the file stored it, carrying the
    ///     <see cref="BaselineEntry.Because" /> recorded with it and the
    ///     <see cref="BaselineEntry.SiteCount" /> it grandfathers.
    /// </summary>
    /// <param name="identity">The identity to look up; its reason and site count play no part in the match.</param>
    /// <param name="stored">The entry as the baseline file stored it, or null when there is no match.</param>
    /// <returns><c>true</c> when this section grandfathers an entry with that identity.</returns>
    /// <remarks>
    ///     An edge from a project to one of its packages or frameworks that has no entry of its own falls back
    ///     to the project-wide entry a file captured before items were keyed apart
    ///     (<see cref="BaselineEntry.ProjectWideForm" />); <paramref name="stored" /> is then that subject
    ///     entry, which is how a caller tells the fallback from an exact match.
    /// </remarks>
    public bool TryMatch(BaselineEntry identity, out BaselineEntry? stored)
    {
        Guard.NotNull(identity, nameof(identity));
        if (_lookup.TryGetValue(identity, out stored)) return true;

        return identity.ProjectWideForm is { } projectWide && _lookup.TryGetValue(projectWide, out stored);
    }
}
