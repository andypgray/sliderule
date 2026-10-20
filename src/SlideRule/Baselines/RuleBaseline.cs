using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>
///     One rule's captured baseline: the set of <see cref="BaselineEntry" /> identities whose
///     violations are grandfathered.
/// </summary>
internal sealed class RuleBaseline
{
    private readonly Dictionary<BaselineEntry, BaselineEntry> _lookup;

    /// <summary>
    ///     Builds a section from entries in any order; entries sharing an identity are collapsed into one as
    ///     <see cref="BaselineEntry.Collapsed" /> collapses them, the rule a write applies too.
    /// </summary>
    internal RuleBaseline(IReadOnlyCollection<BaselineEntry> entries)
    {
        Guard.NotNull(entries, nameof(entries));
        _lookup = BaselineEntry.Collapsed(entries)
            .ToDictionary(entry => entry, entry => entry);

        Entries = BaselineEntry.InCanonicalOrder(_lookup.Values);
    }

    public IReadOnlyList<BaselineEntry> Entries { get; }

    public int Count => Entries.Count;

    /// <summary>
    ///     Looks up the stored entry whose identity matches <paramref name="identity" />, such as the one
    ///     <c>Violation.BaselineIdentity()</c> returns. The match ignores the reason and the site count, so
    ///     <paramref name="stored" /> is the entry as the file records it, carrying both.
    /// </summary>
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
