namespace SlideRule.Internal;

/// <summary>
///     The directory path globs are relative to (GRAMMAR §4.2): the solution directory on every host that loads
///     a solution, and the model file's directory in the build analyzer, which renders the file beside the
///     solution. It turns a declaring file's path into the segments a <see cref="PathGlob" /> matches.
/// </summary>
/// <remarks>
///     <para>
///         Text arithmetic and nothing else, on <c>BaselinePathResolver</c>'s grounds: a reader running inside the
///         compiler has no current directory worth consulting, and the <c>System.IO.Path</c> members either read
///         one or, on .NET Framework, throw on a character they dislike. Both <c>/</c> and <c>\</c> separate on
///         every operating system, an empty or <c>.</c> segment is dropped, and a <c>..</c> folds into the segment
///         before it, so a path the build wrote with mixed separators and one an author would write compare alike.
///     </para>
///     <para>
///         The root is compared segment by segment ignoring case, as the globs are matched, and a file outside it,
///         or a path with no segments at all, is under no glob: <see cref="Relative" /> answers null.
///     </para>
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

    // The path's segments with every empty and `.` segment dropped and each `..` folded into the segment before
    // it; a `..` with nothing before it is dropped, as one at the root stays at the root.
    private static List<string> Folded(string path)
    {
        var segments = new List<string>();
        foreach (string segment in path.Split('/', '\\'))
        {
            if (segment.Length == 0 || segment == ".") continue;

            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments;
    }
}
