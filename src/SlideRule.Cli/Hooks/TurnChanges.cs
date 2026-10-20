namespace SlideRule.Cli.Hooks;

/// <summary>Which code files changed between two snapshots of one working tree, by their bytes.</summary>
/// <remarks>
///     A file's bytes are what count, not its timestamp and not HEAD. A file rewritten with the same bytes has not
///     changed, and neither has a file whose bytes a commit took in as they were. The cost is the other direction:
///     bytes cannot say who wrote them, so another session's write during the turn reads as a change like any other.
/// </remarks>
internal static class TurnChanges
{
    /// <summary>
    ///     The root-relative code paths whose bytes differ between <paramref name="before" /> and
    ///     <paramref name="after" />, in first-seen order, or null when that cannot be told.
    /// </summary>
    /// <remarks>
    ///     <paramref name="committed" /> lists the paths that differ between the two HEADs, and is null when they
    ///     are one commit or git could not say, which under a moved HEAD reads as doubt.
    /// </remarks>
    public static IReadOnlyList<string>? Between(
        TreeSnapshot before, TreeSnapshot after, IReadOnlyList<string>? committed, Func<string, string> digestNow)
    {
        if (!before.Known || !after.Known) return null;

        bool moved = !string.Equals(before.Head, after.Head, StringComparison.Ordinal);
        if (moved && committed is null) return null;

        HashSet<string> committedCode = moved
            ? committed!.Where(HookCodeFiles.IsCode)
                .ToHashSet(StringComparer.Ordinal)
            : [];
        IEnumerable<string> candidates = before.Paths
            .Concat(after.Paths)
            .Concat(committedCode)
            .Distinct(StringComparer.Ordinal);

        return candidates.Where(path => Changed(path, before, after, committedCode, digestNow))
            .ToList();
    }

    // A file the earlier snapshot did not record was HEAD's version, which it still is unless git lists it now or a
    // commit between the two HEADs changed it.
    private static bool Changed(
        string path,
        TreeSnapshot before,
        TreeSnapshot after,
        HashSet<string> committedCode,
        Func<string, string> digestNow)
    {
        if (before.DigestOf(path) is not { } was) return after.DigestOf(path) is not null || committedCode.Contains(path);

        string now = after.DigestOf(path) ?? digestNow(path);
        return was == TreeSnapshot.Unreadable || !string.Equals(was, now, StringComparison.Ordinal);
    }
}
