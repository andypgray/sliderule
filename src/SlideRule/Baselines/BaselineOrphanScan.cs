namespace SlideRule.Baselines;

/// <summary>
///     What a scan of a solution's baseline files found beyond what the spec's rules read: the sections filed
///     under an ID no rule declares, and the files that could not be read at all.
/// </summary>
internal sealed class BaselineOrphanScan
{
    /// <summary>
    ///     A scan result of <paramref name="orphans" /> and <paramref name="unreadable" />, each in scan order.
    /// </summary>
    internal BaselineOrphanScan(IReadOnlyList<BaselineOrphan> orphans, IReadOnlyList<UnreadableBaseline> unreadable)
    {
        Orphans = orphans;
        Unreadable = unreadable;
    }

    /// <summary>
    ///     Gets the scan that found nothing: what every index carries until a host that reads the baseline files
    ///     from disk attaches a real one.
    /// </summary>
    internal static BaselineOrphanScan Empty { get; } = new([], []);

    /// <summary>Gets the sections filed under a rule ID the spec does not declare, in scan order.</summary>
    internal IReadOnlyList<BaselineOrphan> Orphans { get; }

    /// <summary>Gets the baseline files that could not be read, in scan order.</summary>
    internal IReadOnlyList<UnreadableBaseline> Unreadable { get; }

    /// <summary>Gets whether the scan found neither an orphan nor an unreadable file.</summary>
    internal bool IsEmpty => Orphans.Count == 0 && Unreadable.Count == 0;
}
