using SlideRule.Internal;

namespace SlideRule.Rendering;

/// <summary>
///     Renders source paths relative to the solution directory with forward slashes: the form the check
///     report, <c>--json</c> output and SARIF output all carry, so a path reads the same whichever
///     machine produced it.
/// </summary>
/// <remarks>
///     <see cref="Relative" /> gives what <c>Path.GetRelativePath(directory, file)</c> gives with
///     backslashes replaced by forward ones. Both arguments are made absolute first, and segments are
///     compared the way the platform compares file names (<see cref="PathComparison" />).
/// </remarks>
// Path.GetRelativePath does not exist on netstandard2.0, so the walk is hand-rolled; PathFormatTests
// pins it equivalent to Path.GetRelativePath(dir, file).Replace('\\','/'), which is what keeps the
// JSON golden and the pinned human report stable across machines.
internal static class PathFormat
{
    private static readonly char[] LeafSeparators = ['/', '\\'];

    /// <summary>
    ///     Returns the forward-slashed path from <paramref name="solutionDirectory" /> to
    ///     <paramref name="filePath" />.
    /// </summary>
    public static string Relative(string solutionDirectory, string filePath)
    {
        return new Relativizer(solutionDirectory).Relative(filePath);
    }

    /// <summary>
    ///     Reports whether <paramref name="directory" /> is the path <paramref name="pathSegments" /> spell, or
    ///     an ancestor of it.
    /// </summary>
    /// <remarks>
    ///     <paramref name="pathSegments" /> is a full path already split by <see cref="Segments" />, so a caller
    ///     asking of many directories normalizes its path once. The directory is made absolute and compared
    ///     segment by segment, the way the platform compares file names, so this and <see cref="Relative" />
    ///     cannot disagree about whether two spellings are one path. Symlinks are not resolved: canonicalize
    ///     first if that matters.
    /// </remarks>
    internal static bool Contains(string directory, string[] pathSegments)
    {
        string[] directorySegments = Segments(Path.GetFullPath(directory));
        return IsWithin(directorySegments, pathSegments);
    }

    // The segment test behind Contains, for a caller that asks it of many paths against one directory it has
    // already split. Card placement keeps a declaration site only when this holds, so a card and the lookup that
    // finds it cannot disagree about which files lie inside a directory.
    internal static bool IsWithin(string[] directorySegments, string[] pathSegments)
    {
        if (directorySegments.Length > pathSegments.Length) return false;

        for (var i = 0; i < directorySegments.Length; i++)
            if (!string.Equals(directorySegments[i], pathSegments[i], PathComparison.Comparison))
                return false;

        return true;
    }

    // How many leading segments two split paths share, compared the way the platform compares file names. The
    // prefix walk behind both the relative form and the deepest common directory of a set of files, so the two
    // cannot disagree about where two paths part company. How far to look is the caller's own invariant —
    // whole paths for one, directory segments only for the other — so the bound comes in rather than being
    // computed here.
    internal static int CommonPrefixLength(string[] a, string[] b, int limit)
    {
        var length = 0;
        while (length < limit && string.Equals(a[length], b[length], PathComparison.Comparison)) length++;

        return length;
    }

    // Split a full path into segments, dropping trailing empties from a trailing separator while keeping a
    // leading empty (a POSIX absolute path's root), so two POSIX absolutes still share their empty root.
    internal static string[] Segments(string fullPath)
    {
        string[] parts = fullPath.Split('/', '\\');
        int end = parts.Length;
        while (end > 0 && parts[end - 1].Length == 0) end--;

        return end == parts.Length ? parts : parts.Take(end).ToArray();
    }

    // Both separators are looked for by hand rather than via Path.GetFileName, which off Windows does not treat
    // '\' as a separator: a path captured on Windows and read on macOS or Linux would come back whole, so the
    // same input has to leave the same leaf on every host.
    internal static string Leaf(string path)
    {
        int separator = path.LastIndexOfAny(LeafSeparators);
        return separator < 0 ? path : path.Substring(separator + 1);
    }

    /// <summary>A base directory analysed once, for a caller relativizing many paths against the same one.</summary>
    // One algorithm, not two: PathFormat.Relative builds one of these and calls it.
    internal sealed class Relativizer
    {
        private readonly string[] _baseSegments;

        // Keyed by the path as recorded: a report's sites repeat a handful of files many times over. One
        // instance serves one render on one thread, so the map takes no lock.
        private readonly Dictionary<string, string> _relative = new(StringComparer.Ordinal);

        public Relativizer(string baseDirectory)
        {
            _baseSegments = Segments(Path.GetFullPath(baseDirectory));
        }

        public string Relative(string filePath)
        {
            if (_relative.TryGetValue(filePath, out string? known)) return known;

            string relative = Compute(filePath);
            _relative[filePath] = relative;
            return relative;
        }

        private string Compute(string filePath)
        {
            string[] toSegments = Segments(Path.GetFullPath(filePath));

            // Different roots (a different drive) have no relative path: fall back to the raw target with
            // slashes normalized.
            if (_baseSegments.Length == 0 || toSegments.Length == 0 ||
                !string.Equals(_baseSegments[0], toSegments[0], PathComparison.Comparison))
                return filePath.Replace('\\', '/');

            int limit = Math.Min(_baseSegments.Length, toSegments.Length);
            int common = CommonPrefixLength(_baseSegments, toSegments, limit);

            var parts = new List<string>();
            for (int i = common; i < _baseSegments.Length; i++) parts.Add("..");
            for (int i = common; i < toSegments.Length; i++) parts.Add(toSegments[i]);

            // Empty means the target IS the directory — Path.GetRelativePath returns "." there.
            return parts.Count == 0 ? "." : string.Join("/", parts);
        }
    }
}
