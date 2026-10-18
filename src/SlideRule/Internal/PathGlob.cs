namespace SlideRule.Internal;

/// <summary>
///     One path glob of the <c>Under</c> adjective (GRAMMAR §4.2): <c>/</c>-separated segments relative to the
///     solution directory, where <c>*</c> matches within a segment and a <c>**</c> segment matches any number of
///     segments, none included. Matching ignores case on every operating system.
/// </summary>
/// <remarks>
///     <para>
///         A glob names a directory as readily as a file, and it reads as a prefix: it matches a path when it
///         matches the path or any of the directories leading to it, so <c>src/Widgets</c> reaches every file
///         under that directory and means exactly what <c>src/Widgets/**</c> means. The prefix reading is a
///         trailing <c>**</c> the matcher appends itself.
///     </para>
///     <para>
///         <see cref="Validate" /> refuses a shape rather than normalizing it, so what an author wrote is what
///         the model carries and what every reader matches with: a rooted glob, a backslash, a <c>.</c> or
///         <c>..</c> segment, an empty segment and a <c>**</c> written into a longer segment all come back as a
///         reason. Nothing here touches the file system or <c>System.IO.Path</c>, so the analyzer can run it
///         inside the compiler.
///     </para>
/// </remarks>
internal sealed class PathGlob
{
    private const string AnySegments = "**";

    private readonly string[] _segments;

    /// <summary>Parses a glob <see cref="Validate" /> accepted. A refused glob is a caller bug and throws.</summary>
    internal PathGlob(string glob)
    {
        Guard.NotNull(glob, nameof(glob));
        if (Validate(glob) is { } reason) throw new ArgumentException($"Path glob '{glob}' {reason}.", nameof(glob));

        _segments = [.. glob.Split('/'), AnySegments];
    }

    /// <summary>
    ///     Why <paramref name="glob" /> is not a path glob, as a clause that completes "The path glob 'x' …",
    ///     or null when it is one.
    /// </summary>
    internal static string? Validate(string glob)
    {
        Guard.NotNull(glob, nameof(glob));
        if (string.IsNullOrWhiteSpace(glob)) return "is blank";
        if (glob.IndexOf('\\') >= 0) return "contains a backslash; separate segments with `/`";
        if (glob[0] == '/' || IsDriveRooted(glob))
            return "is rooted; a path glob is relative to the solution directory";

        foreach (string segment in glob.Split('/'))
        {
            if (segment.Length == 0) return "has an empty segment; write one `/` between segments and none at the end";
            if (segment is "." or "..") return "has a `.` or `..` segment; name the directories from the solution directory down";
            if (segment != AnySegments && segment.Contains(AnySegments))
                return "puts `**` inside a segment; `**` stands alone between slashes";
        }

        return null;
    }

    /// <summary>
    ///     Whether the glob matches a path given as its segments relative to the solution directory, the file
    ///     name last — the path itself or any directory leading to it.
    /// </summary>
    internal bool Matches(IReadOnlyList<string> relativeSegments)
    {
        Guard.NotNull(relativeSegments, nameof(relativeSegments));

        // g/s walk the glob and the path; star/mark remember the last `**` taken and how far the path had advanced
        // then, so a dead end backtracks by letting that `**` take one more segment. Exact because every other glob
        // segment consumes exactly one path segment, as Wildcard's characters do.
        var g = 0;
        var s = 0;
        int star = -1;
        var mark = 0;

        while (s < relativeSegments.Count)
            if (g < _segments.Length && _segments[g] == AnySegments)
            {
                star = g++;
                mark = s;
            }
            else if (g < _segments.Length && Wildcard.Match(_segments[g], relativeSegments[s], ignoreCase: true))
            {
                g++;
                s++;
            }
            else if (star >= 0)
            {
                g = star + 1;
                s = ++mark;
            }
            else
            {
                return false;
            }

        while (g < _segments.Length && _segments[g] == AnySegments) g++;

        return g == _segments.Length;
    }

    private static bool IsDriveRooted(string glob)
    {
        return glob.Length >= 2 && glob[1] == ':' && char.IsLetter(glob[0]);
    }
}
