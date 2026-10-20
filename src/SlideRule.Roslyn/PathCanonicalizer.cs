namespace SlideRule.Roslyn;

/// <summary>
///     A managed <c>realpath</c>: resolves a path to a symlink-free, fully-qualified spelling so that paths
///     obtained different ways can be compared as strings. A workspace's document paths keep whatever
///     spelling the solution was opened with, while a path from another tool may already be
///     symlink-resolved; on a symlinked root (macOS's <c>/var</c> against <c>/private/var</c>, a symlinked
///     home directory, a Windows junction) the two never match, and a prefix test silently misses.
/// </summary>
// It lives in this assembly rather than in Core because FileSystemInfo.ResolveLinkTarget is net6+ and
// Core is netstandard2.0.
internal static class PathCanonicalizer
{
    // A backstop against a pathological reparse graph (mutually recursive symlinks): real filesystems converge
    // in one or two passes, and a genuine cycle throws inside ResolveLinkTarget, which reads as not a link.
    private const int MaxIterations = 40;

    /// <summary>
    ///     Returns <paramref name="path" />, absolute or relative to the current directory, made absolute and
    ///     symlink-free, following a symlinked directory anywhere along it rather than only at the end. A path with
    ///     no symlinks in it comes back exactly as <see cref="System.IO.Path.GetFullPath(string)" /> would return it.
    ///     A path that does not exist is still made absolute and a symlinked directory above it is still followed;
    ///     nothing here reports whether the path exists, so callers keep their own existence checks. An empty path
    ///     is returned unchanged.
    /// </summary>
    public static string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        string current = Path.GetFullPath(path);
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            string? resolved = ResolveDeepestSymlink(current);
            if (resolved is null) return current; // no symlinked ancestor left — fixed point reached

            current = Path.GetFullPath(resolved);
        }

        return current;
    }

    /// <summary>
    ///     Whether <paramref name="path" /> is itself a reparse point, for a caller that has resolved every
    ///     ancestor and needs to know whether the leaf can be reattached as is. A symlinked <c>.csproj</c>
    ///     answers true exactly as a symlinked directory does.
    /// </summary>
    internal static bool IsLink(string path)
    {
        return TryResolveLinkTarget(new DirectoryInfo(path)) is not null;
    }

    // The path with its deepest symlinked ancestor replaced by that link's final target and the remainder
    // reattached, or null when no ancestor is a symlink.
    private static string? ResolveDeepestSymlink(string current)
    {
        for (DirectoryInfo? dir = new(current); dir is not null; dir = dir.Parent)
        {
            if (TryResolveLinkTarget(dir) is not { } target) continue;

            string remainder = Path.GetRelativePath(dir.FullName, current);
            return remainder == "." ? target : Path.Combine(target, remainder);
        }

        return null;
    }

    // ResolveLinkTarget probes the path as a reparse point, which throws on drive roots ("C:\"),
    // access-denied ancestors, and nonexistent paths; a throw here just means "not a resolvable link,
    // keep walking up".
    private static string? TryResolveLinkTarget(DirectoryInfo dir)
    {
        try
        {
            return dir.ResolveLinkTarget(true)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
