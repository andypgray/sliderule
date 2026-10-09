namespace SlideRule.Cli.Hooks;

/// <summary>
///     Which code files changed between two snapshots of one working tree, by their bytes: the question both of the
///     hook's skips ask, and the answer a check's tripwires are scoped to.
/// </summary>
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
    /// <param name="before">The earlier snapshot: a baseline or the tree a verdict covered.</param>
    /// <param name="after">The snapshot taken now.</param>
    /// <param name="committed">
    ///     The paths that differ between the two snapshots' HEADs, when those differ; null when they are the same
    ///     commit, or when git could not say, which a moved HEAD reads as doubt.
    /// </param>
    /// <param name="digestNow">The digest of a root-relative path's bytes as they are now.</param>
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

    // A file the earlier snapshot recorded is compared by its bytes, wherever HEAD has gone since, so a commit that
    // took the file in as it stood changes nothing. A file it did not record was HEAD's version, which it still is
    // unless git lists it now or a commit between the two HEADs changed it.
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
