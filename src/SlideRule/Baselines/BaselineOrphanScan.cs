namespace SlideRule.Baselines;

/// <summary>
///     What a scan of a solution's baseline files found beyond what the spec's rules read: the sections filed
///     under an ID no rule declares, and the files that could not be read at all, each list in scan order.
/// </summary>
internal sealed class BaselineOrphanScan
{
    internal BaselineOrphanScan(IReadOnlyList<BaselineOrphan> orphans, IReadOnlyList<UnreadableBaseline> unreadable)
    {
        Orphans = orphans;
        Unreadable = unreadable;
    }

    internal static BaselineOrphanScan Empty { get; } = new([], []);

    internal IReadOnlyList<BaselineOrphan> Orphans { get; }

    internal IReadOnlyList<UnreadableBaseline> Unreadable { get; }

    internal bool IsEmpty => Orphans.Count == 0 && Unreadable.Count == 0;
}
