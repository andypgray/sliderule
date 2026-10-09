using SlideRule.Checking;
using SlideRule.Codebase;

namespace SlideRule.Rendering;

/// <summary>
///     Scoped context placement, end to end: given a selection, where does its card go? The whole step —
///     collecting the declaration-site file paths of the solution-declared types it matches, keeping the ones
///     inside the solution directory, then collapsing those to their <em>deepest common ancestor directory</em>
///     — the directory whose <c>AGENTS.md</c> receives that selection's card.
/// </summary>
/// <remarks>
///     <para>
///         Both <see cref="ScopedContextResolver" /> (scopes of either posture) and
///         <see cref="LayerContextResolver" /> (layer local-rules cards) place through this one helper, from
///         path collection to collapse, so the two emission keys land a co-located card in exactly the same
///         directory.
///     </para>
///     <para>
///         The solution directory bounds every card. A project can compile a file from outside the checkout (a
///         source-only package's file stays in the package folder), and collapsing that file with the project's
///         own would put the card in a directory above both, where every checkout beneath it reads the card. A file
///         outside the boundary is therefore left out of the collapse rather than refused: the card lands where
///         the files inside put it, and a selection with no file inside is skipped with a reason of its own.
///     </para>
///     <para>
///         Where several projects compile one type, the sites counted are those of the declarers the selection
///         names the type at (GRAMMAR §4.1). A project-defined layer therefore counts its own copy of a name its
///         sibling projects also compile, and a glob or type head counts every copy.
///     </para>
/// </remarks>
internal static class DirectoryPlacement
{
    /// <summary>
    ///     Where <paramref name="selection" />'s card belongs: a directory that always lies inside
    ///     <paramref name="boundary" />, or the reason there is none, worded for the emission key that asked:
    ///     <paramref name="kind" /> (<c>scope</c> or <c>layer</c>) and its <paramref name="name" />.
    /// </summary>
    internal static CardDestination Place(
        SelectionEvaluator evaluator, Selection selection, Boundary boundary, string kind, string name)
    {
        var inside = new SortedSet<string>(StringComparer.Ordinal);
        var anySite = false;

        SelectionAdmission admission = SelectionAdmission.Collect(evaluator, selection, SelectionPosition.Subject);
        foreach (TypeNode type in admission.Nodes)
        {
            if (type.IsExternal) continue;

            foreach (SourceLocation site in admission.CountedSitesOf(type))
            {
                anySite = true;
                if (boundary.InsideFullPath(site.FilePath) is { } fullPath) inside.Add(fullPath);
            }
        }

        if (!anySite) return CardDestination.Skipped(SkipReason(kind, name, "no types"));
        if (inside.Count == 0)
            return CardDestination.Skipped(SkipReason(kind, name, "only types declared outside the solution directory"));

        // Never null here: every path left shares the boundary's segments, and the boundary is absolute, so the
        // files share at least its root. The same reason puts the answer inside the boundary.
        return CardDestination.Into(DeepestCommonDirectory(inside.ToList())!);
    }

    // The deepest directory that contains every file: the longest common prefix of the files' directory segments,
    // compared the way the platform compares file names and spelled the way the first file spells it. Null where
    // the files share no root at all (two drives, two servers, two relative trees). A prefix that is nothing but a
    // one-segment root is rebuilt with its separator, because a bare "C:" names that drive's working directory and
    // an empty prefix names nothing; a share keeps its server-and-share form, which is already a directory.
    internal static string? DeepestCommonDirectory(IReadOnlyList<string> filePaths)
    {
        // Only the first path's segments are ever read back, and the answer can only shrink, so the rest
        // are split one at a time and abandoned — and the walk stops the moment two paths share no root
        // at all, however many sites are still queued behind them.
        string[] prefix = filePaths[0].Split('/', '\\');
        int common = DirectorySegmentCount(prefix);
        for (var i = 1; i < filePaths.Count && common > 0; i++)
        {
            string[] segments = filePaths[i].Split('/', '\\');

            // Both bounds are directory-segment counts, so the file names sitting past them are never compared.
            int limit = Math.Min(common, DirectorySegmentCount(segments));
            common = PathFormat.CommonPrefixLength(prefix, segments, limit);
        }

        int root = RootSegmentCount(prefix);
        if (common == 0 || common < root) return null;

        char separator = filePaths[0].IndexOf('\\') >= 0 ? '\\' : '/';
        if (root == 1 && common == 1) return prefix[0] + separator;

        return string.Join(separator.ToString(), prefix.Take(common));
    }

    // One frame, one cause, and both shared by the two emission keys, so a reader who meets a skip under one
    // meets the same words under the other.
    private static string SkipReason(string kind, string name, string cause)
    {
        return $"{kind} '{name}' matched {cause}; no scoped context emitted";
    }

    // A split path's directory portion is everything but the file name (the last segment). Empties are
    // kept so a leading separator (POSIX absolute paths) survives as a leading empty segment that rejoins
    // to a leading separator.
    private static int DirectorySegmentCount(string[] segments)
    {
        return segments.Length - 1;
    }

    // How many leading segments are the root itself: four for a share (two empties, the server, the share), one
    // for a POSIX root or a drive, and none for a relative path, whose first directory is already a directory.
    private static int RootSegmentCount(string[] segments)
    {
        if (segments.Length > 1 && segments[0].Length == 0 && segments[1].Length == 0) return 4;

        return segments[0].Length == 0 || segments[0].EndsWith(":", StringComparison.Ordinal) ? 1 : 0;
    }

    /// <summary>
    ///     The solution directory every card must land inside, analysed once for a caller placing several
    ///     cards against it.
    /// </summary>
    /// <remarks>
    ///     Splitting the directory and deciding a recorded path's verdict are both constant in the card, so a
    ///     caller resolving both emission keys for one codebase builds one of these and hands it to both
    ///     resolvers — and a file two cards share is normalized and split once for the run rather than once
    ///     per card. Built where the evaluator is, and for the same reason.
    /// </remarks>
    internal sealed class Boundary(string solutionDirectory)
    {
        private readonly string[] _segments = PathFormat.Segments(Path.GetFullPath(solutionDirectory));
        private readonly Dictionary<string, string?> _verdicts = new(StringComparer.Ordinal);

        /// <summary>
        ///     One recorded declaration site's full path when it lies inside the boundary, and null when it
        ///     does not.
        /// </summary>
        /// <remarks>
        ///     Memoized per recorded path, because a file declaring several types, or several parts of one, is
        ///     recorded once per declaration.
        /// </remarks>
        internal string? InsideFullPath(string recordedPath)
        {
            if (_verdicts.TryGetValue(recordedPath, out string? verdict)) return verdict;

            string fullPath = Path.GetFullPath(recordedPath);
            string[] segments = PathFormat.Segments(fullPath);
            verdict = PathFormat.IsWithin(_segments, segments) ? fullPath : null;
            _verdicts[recordedPath] = verdict;
            return verdict;
        }
    }
}
