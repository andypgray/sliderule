using SlideRule.Internal;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     Recognizes the workspace's failure for a project the run lists among its unsupported projects: for a Web
///     Site, a project file it could not find at the entry's folder or URL joined onto the solution directory; for
///     a project it opens in no language, a project file whose extension it maps to none. Also finds the projects
///     in another language that such failures name and no solution entry declares.
/// </summary>
/// <remarks>
///     A message is read for the paths in it, never for its text, which the workspace localizes. For a solution
///     entry the path is the one <see cref="SolutionProjectFileParser" /> resolves the entry to, the same join the
///     workspace makes.
/// </remarks>
internal static class UnsupportedEntryMessages
{
    /// <summary>
    ///     The <paramref name="messages" /> that name none of the entries in <paramref name="unsupported" /> whose
    ///     kind is one of <paramref name="kinds" />.
    /// </summary>
    internal static List<string> Without(
        IEnumerable<string> messages, IEnumerable<UnsupportedProject> unsupported, params UnsupportedProjectKind[] kinds)
    {
        IReadOnlyList<string> paths = PathsOf(unsupported, kinds);
        return messages
            .Where(message => !IsAbout(message, paths))
            .ToList();
    }

    // The resolved paths of the entries whose kind is one of kinds.
    private static IReadOnlyList<string> PathsOf(
        IEnumerable<UnsupportedProject> unsupported, UnsupportedProjectKind[] kinds)
    {
        return unsupported
            .Where(project => kinds.Contains(project.Kind))
            .Select(project => project.Path)
            .ToList();
    }

    /// <summary>
    ///     Whether <paramref name="message" /> names one of <paramref name="entryPaths" /> whole, so a message about
    ///     a project file inside a Web Site's folder, or under a sibling folder whose name starts with the entry's,
    ///     is not taken for one about the entry.
    /// </summary>
    /// <remarks>
    ///     A path is whole where a quote mark, a line break or the message's end closes it. Followed by anything
    ///     else, a space included, it is whole only while no directory separator follows on its line, since a
    ///     folder name may carry a space or a parenthesis. A match that cannot be told apart is taken for a
    ///     longer path, because a message wrongly kept is one extra line while one wrongly dropped hides a
    ///     project that did not load.
    /// </remarks>
    internal static bool IsAbout(string message, IReadOnlyList<string> entryPaths)
    {
        foreach (string path in entryPaths)
        {
            int start = message.IndexOf(path, PathComparison.Comparison);
            while (start >= 0)
            {
                if (EndsWhole(message, start + path.Length)) return true;

                start = message.IndexOf(path, start + 1, PathComparison.Comparison);
            }
        }

        return false;
    }

    /// <summary>
    ///     The project files in another language that <paramref name="messages" /> name, distinct and in the order
    ///     first named: every quoted absolute path the "ends in <c>proj</c>" partition admits, other than a
    ///     <c>.csproj</c>.
    /// </summary>
    /// <remarks>
    ///     The workspace drops a <c>ProjectReference</c> to a project it opens in no language, and its failure to
    ///     open it is the only trace the load leaves, so this is how a referenced project that no solution entry
    ///     declares is listed. A path holding a quote mark is split and not found; its failure then still reads as
    ///     the load's own.
    /// </remarks>
    internal static IReadOnlyList<string> OtherLanguageProjectFiles(IEnumerable<string> messages)
    {
        return messages
            .SelectMany(message => message.Split(Quotes))
            .Where(IsOtherLanguageProjectFile)
            .Distinct(PathComparison.Comparer)
            .ToList();
    }

    /// <summary>
    ///     Whether <paramref name="segment" /> is a rooted path to a project file in a language other than C#.
    /// </summary>
    internal static bool IsOtherLanguageProjectFile(string segment)
    {
        return Path.IsPathRooted(segment)
               && SolutionProjectFileParser.IsProjectEntry(segment)
               && !segment.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EndsWhole(string message, int end)
    {
        if (end == message.Length) return true;

        char next = message[end];
        if (ContinuesAPath(next)) return false;
        if (ClosingQuotes.IndexOf(next) >= 0) return true;

        int lineEnd = message.IndexOfAny(['\r', '\n'], end);
        string rest = lineEnd < 0 ? message.Substring(end) : message.Substring(end, lineEnd - end);
        return rest.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;
    }

    private static bool ContinuesAPath(char next)
    {
        return char.IsLetterOrDigit(next)
               || next is '.' or '_' or '-'
               || next == Path.DirectorySeparatorChar
               || next == Path.AltDirectorySeparatorChar;
    }

    // The workspace's messages are localized, and each language quotes a path its own way.
    private const string ClosingQuotes = "'\"\u2018\u2019\u201C\u201D\u00AB\u00BB\u201E";

    // Opening and closing marks alike, since a path is the run between two of them.
    private static readonly char[] Quotes = (ClosingQuotes + "\u201A\u201B\u201F").ToCharArray();
}
