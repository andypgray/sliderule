using SlideRule.Internal;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     Recognizes the workspace's failure for a solution's Web Site entries, which the load reports as a project
///     file it could not find at the entry's folder or URL joined onto the solution directory. The run lists each
///     such entry under its unsupported projects by name, so the failure says nothing a reader can act on and
///     names a path that does not exist.
/// </summary>
/// <remarks>
///     The message is recognized by the path in it, never by its text, which the workspace localizes. The path is
///     the one <see cref="SolutionProjectFileParser" /> resolves the entry to, the same join the workspace makes.
/// </remarks>
internal static class WebSiteEntries
{
    /// <summary>The resolved paths of the Web Site entries among <paramref name="unsupported" />.</summary>
    internal static IReadOnlyList<string> PathsOf(IEnumerable<UnsupportedProject> unsupported)
    {
        return unsupported
            .Where(project => project.Kind == UnsupportedProjectKind.WebSite)
            .Select(project => project.Path)
            .ToList();
    }

    /// <summary>
    ///     Whether <paramref name="message" /> names one of <paramref name="webSitePaths" /> whole, so a message about
    ///     a project file inside a Web Site's folder is not taken for one about the folder.
    /// </summary>
    internal static bool IsAbout(string message, IReadOnlyList<string> webSitePaths)
    {
        foreach (string path in webSitePaths)
        {
            int start = message.IndexOf(path, PathComparison.Comparison);
            while (start >= 0)
            {
                int end = start + path.Length;
                if (end == message.Length || !ContinuesAPath(message[end])) return true;

                start = message.IndexOf(path, start + 1, PathComparison.Comparison);
            }
        }

        return false;
    }

    private static bool ContinuesAPath(char next)
    {
        return char.IsLetterOrDigit(next)
               || next is '.' or '_' or '-'
               || next == Path.DirectorySeparatorChar
               || next == Path.AltDirectorySeparatorChar;
    }
}
