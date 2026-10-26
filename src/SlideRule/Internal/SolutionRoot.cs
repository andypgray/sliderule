namespace SlideRule.Internal;

/// <summary>
///     The directory path globs are relative to (GRAMMAR §4.2): the solution directory on every host that loads
///     a solution, and the model file's directory in the build analyzer, which renders the file beside the
///     solution. It turns a declaring file's path into the segments a <see cref="PathGlob" /> matches.
/// </summary>
/// <remarks>
///     Text arithmetic and nothing else, on <c>BaselinePathResolver</c>'s grounds: a reader running inside the
///     compiler has no current directory worth consulting, and the <c>System.IO.Path</c> members either read one
///     or, on .NET Framework, throw on a character they dislike. Both <c>/</c> and <c>\</c> separate on every
///     operating system, so a path the build wrote with mixed separators and one an author would write compare
///     alike. The root is compared ignoring case, as the globs are matched.
/// </remarks>
internal sealed class SolutionRoot
{
    private readonly List<string> _segments;

    /// <summary>A root at <paramref name="directory" />, an absolute path in either separator dialect.</summary>
    internal SolutionRoot(string directory)
    {
        _segments = Folded(Guard.NotNull(directory, nameof(directory)));
    }

    /// <summary>
    ///     <paramref name="filePath" />'s segments below the root, the file name last, or null where the file is not
    ///     below it.
    /// </summary>
    internal IReadOnlyList<string>? Relative(string filePath)
    {
        Guard.NotNull(filePath, nameof(filePath));

        List<string> segments = Folded(filePath);
        if (segments.Count <= _segments.Count) return null;

        for (var i = 0; i < _segments.Count; i++)
            if (!string.Equals(segments[i], _segments[i], StringComparison.OrdinalIgnoreCase))
                return null;

        return segments.GetRange(_segments.Count, segments.Count - _segments.Count);
    }

    // Both separators split on every operating system, and an empty segment is no segment.
    private static List<string> Folded(string path)
    {
        IEnumerable<string> segments = path.Split('/', '\\').Where(segment => segment.Length > 0);
        return PathSegments.Fold(segments, anchored: true);
    }
}
