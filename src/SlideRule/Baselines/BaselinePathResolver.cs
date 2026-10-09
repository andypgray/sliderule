using System.Text;
using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>
///     Resolves the baseline path a spec author wrote — forward slashes, usually relative — against the
///     directory of the file it was read beside, by splitting, folding and joining strings and nothing
///     else.
/// </summary>
/// <remarks>
///     <para>
///         Keep the arithmetic on the text: a reader running inside the C# compiler has no current
///         directory worth consulting and must not throw on what a file says, and the
///         <c>System.IO.Path</c> members either read the process's current directory or, on .NET
///         Framework, throw on a character they dislike.
///     </para>
///     <para>
///         The platform's own separator picks the dialect. On Windows both <c>/</c> and <c>\</c>
///         separate and the answer is written with <c>\</c>; elsewhere only <c>/</c> separates and a
///         backslash is an ordinary character in a file name. A rooted path wins over the base
///         directory, a <c>..</c> at the root stays at the root, doubled separators collapse, and the
///         answer carries no trailing separator unless it is a bare root. Nothing here throws for its
///         input; a null argument is a caller bug and does.
///     </para>
///     <para>
///         It matches <c>Path.GetFullPath(path, baseDirectory)</c> on everything a spec is likely to
///         carry. Where it deliberately does not: a trailing separator is dropped rather than kept, a
///         drive-relative <c>C:x</c> is read as <c>C:\x</c> rather than against that drive's current
///         directory, a segment's trailing dots and spaces are kept rather than trimmed the way Win32
///         trims them, and text <c>GetFullPath</c> rejects outright comes back as an answer.
///     </para>
/// </remarks>
internal static class BaselinePathResolver
{
    private static readonly bool Windows = Path.DirectorySeparatorChar == '\\';
    private static readonly string Separator = Path.DirectorySeparatorChar.ToString();

    /// <summary>
    ///     The absolute path <paramref name="baselinePath" /> names relative to
    ///     <paramref name="baseDirectory" />, folded and with its separators made uniform. A rooted
    ///     <paramref name="baselinePath" /> is returned folded on its own; one that begins with a
    ///     separator but names no drive takes the base directory's root and nothing else of it.
    /// </summary>
    /// <remarks>
    ///     The answer is absolute only where <paramref name="baseDirectory" /> is: a relative base
    ///     yields a relative answer rather than a refusal, there being no current directory here to
    ///     appeal to.
    /// </remarks>
    internal static string Resolve(string baselinePath, string baseDirectory)
    {
        PathParts path = Parse(Guard.NotNull(baselinePath, nameof(baselinePath)));
        if (path.Root.Length > 0) return Join(path.Root, Fold(path.Segments, anchored: true));

        PathParts basis = Parse(Guard.NotNull(baseDirectory, nameof(baseDirectory)));
        if (path.RootRelative)
        {
            string root = basis.Root.Length > 0 ? basis.Root : Separator;
            return Join(root, Fold(path.Segments, anchored: true));
        }

        var combined = new List<string>(basis.Segments);
        combined.AddRange(path.Segments);
        string prefix = Prefix(basis);
        return Join(prefix, Fold(combined, prefix.Length > 0));
    }

    /// <summary>
    ///     <paramref name="absolutePath" /> under the same folding and separator rule
    ///     <see cref="Resolve" /> applies, so both sides of a comparison can be put through one
    ///     normalizer.
    /// </summary>
    internal static string Normalize(string absolutePath)
    {
        PathParts parts = Parse(Guard.NotNull(absolutePath, nameof(absolutePath)));
        string prefix = Prefix(parts);
        return Join(prefix, Fold(parts.Segments, prefix.Length > 0));
    }

    /// <summary>
    ///     The directory part of <paramref name="filePath" />, under the same rules — the last segment
    ///     dropped, the rest folded. A bare file name has no directory and comes back empty.
    /// </summary>
    /// <remarks>
    ///     The argument is a file path, so a trailing separator marks no directory: it is one more empty
    ///     segment and collapses like any other, leaving the segment before it to be dropped.
    /// </remarks>
    internal static string DirectoryOf(string filePath)
    {
        PathParts parts = Parse(Guard.NotNull(filePath, nameof(filePath)));
        string prefix = Prefix(parts);
        List<string> folded = Fold(parts.Segments, prefix.Length > 0);
        if (folded.Count > 0) folded.RemoveAt(folded.Count - 1);

        return Join(prefix, folded);
    }

    private static PathParts Parse(string path)
    {
        if (!Windows)
        {
            bool rooted = path.Length > 0 && path[0] == '/';
            return new PathParts(rooted ? "/" : string.Empty, false, SegmentsOf(path, rooted ? 1 : 0));
        }

        // Two separators lead a UNC path, whose server and share are part of the root rather than
        // segments: a `..` beneath one stops there, as it does beneath a drive.
        if (path.Length >= 2 && IsSeparator(path[0]) && IsSeparator(path[1])) return ParseUnc(path);

        if (path.Length >= 2 && path[1] == ':' && IsDriveLetter(path[0]))
        {
            string drive = path.Substring(0, 2) + Separator;
            // A drive with no separator behind it is drive-relative, which on Windows means "that
            // drive's current directory". There is none to read here, so the drive is taken as the root.
            int start = path.Length >= 3 && IsSeparator(path[2]) ? 3 : 2;
            return new PathParts(drive, false, SegmentsOf(path, start));
        }

        if (path.Length >= 1 && IsSeparator(path[0])) return new PathParts(string.Empty, true, SegmentsOf(path, 1));

        return new PathParts(string.Empty, false, SegmentsOf(path, 0));
    }

    private static PathParts ParseUnc(string path)
    {
        List<string> segments = SegmentsOf(path, 2);
        var root = new StringBuilder(Separator + Separator);
        int taken = Math.Min(2, segments.Count);
        for (var i = 0; i < taken; i++)
        {
            if (i > 0) root.Append(Separator);
            root.Append(segments[i]);
        }

        segments.RemoveRange(0, taken);
        return new PathParts(root.ToString(), false, segments);
    }

    // Every run of non-separator characters from `start` on. An empty run is no segment at all, which is
    // what collapses a doubled separator and drops a trailing one.
    private static List<string> SegmentsOf(string path, int start)
    {
        var segments = new List<string>();
        int from = start;
        for (int i = start; i <= path.Length; i++)
        {
            bool boundary = i == path.Length || IsSeparator(path[i]);
            if (!boundary) continue;

            if (i > from) segments.Add(path.Substring(from, i - from));
            from = i + 1;
        }

        return segments;
    }

    private static List<string> Fold(IEnumerable<string> segments, bool anchored)
    {
        var folded = new List<string>();
        foreach (string segment in segments)
        {
            if (string.Equals(segment, ".", StringComparison.Ordinal)) continue;

            if (!string.Equals(segment, "..", StringComparison.Ordinal))
            {
                folded.Add(segment);
                continue;
            }

            int last = folded.Count - 1;
            bool climbable = last >= 0 && !string.Equals(folded[last], "..", StringComparison.Ordinal);
            if (climbable)
            {
                folded.RemoveAt(last);
                continue;
            }

            // A `..` at the root stays at the root. Above a path with no root it is kept instead, there
            // being nothing there to stop at and a dropped one naming a different directory.
            if (!anchored) folded.Add("..");
        }

        return folded;
    }

    private static string Prefix(PathParts parts)
    {
        if (parts.Root.Length > 0) return parts.Root;

        return parts.RootRelative ? Separator : string.Empty;
    }

    private static string Join(string prefix, IReadOnlyList<string> segments)
    {
        string body = string.Join(Separator, segments);
        if (prefix.Length == 0) return body;
        if (body.Length == 0) return prefix;

        bool separated = IsSeparator(prefix[prefix.Length - 1]);
        return separated ? prefix + body : prefix + Separator + body;
    }

    private static bool IsSeparator(char c)
    {
        return c == '/' || (Windows && c == '\\');
    }

    private static bool IsDriveLetter(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }

    /// <summary>A path split into the root it is anchored to and the segments beneath it.</summary>
    /// <remarks>
    ///     <c>Root</c> is empty on a path that names none, and <c>RootRelative</c> is the one case where
    ///     that is not the whole story: a Windows path leading with a separator and no drive is anchored
    ///     to a root it does not name, which is the base directory's.
    /// </remarks>
    private readonly struct PathParts(string root, bool rootRelative, List<string> segments)
    {
        public string Root { get; } = root;
        public bool RootRelative { get; } = rootRelative;
        public List<string> Segments { get; } = segments;
    }
}
