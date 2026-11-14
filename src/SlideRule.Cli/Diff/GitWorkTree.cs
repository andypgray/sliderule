using SlideRule.Internal;

namespace SlideRule.Cli.Diff;

/// <summary>
///     Which git work tree a directory belongs to, as git itself answers it, so every command that needs a
///     repository root asks the same question. Null means no work tree, or git could not say.
/// </summary>
internal static class GitWorkTree
{
    /// <summary>
    ///     The top level of the working tree <paramref name="directory" /> belongs to, as git spells it, or null
    ///     when it belongs to none. A linked worktree answers with its own root.
    /// </summary>
    public static async Task<string?> TopLevelAsync(string directory, CancellationToken ct)
    {
        string[]? lines = await GitProcess.AnswerLinesAsync(directory, ct, "rev-parse", "--show-toplevel");
        return lines is [{ Length: > 0 } topLevel, ..] ? topLevel : null;
    }

    /// <summary>
    ///     Where <paramref name="directory" /> lies in the work tree it belongs to, or null when git names none, or
    ///     when the ancestor that git's path below the root leads back to is not, to git, that work tree's top level.
    /// </summary>
    // The root is cut from the caller's own spelling rather than taken from git's, because git answers a substituted
    // drive with its target, a mapped network drive with its share's UNC path (verified 2026-11-10, git
    // 2.56.0.windows.1) and a link with the directory it leads to, while the model's sites keep the spelling the
    // solution was reached by. The names below the root are git's, because git spells them as the disk does, letter
    // case included, and they are the names another clone has. The cut counts segments and compares no names, so git
    // is asked again at the candidate, and only its answer that the candidate is the same top level makes it the root.
    // A directory inside a .git directory has a prefix and no work tree, and asking for the top level is what makes it
    // answer null.
    public static async Task<Location?> LocateAsync(string directory, CancellationToken ct)
    {
        if (await TopLevelAndPrefixAsync(directory, ct) is not var (topLevel, below)) return null;

        DirectoryInfo? root = new(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)));
        for (var i = 0; i < below.Length && root is not null; i++) root = root.Parent;

        if (root is null) return null;
        if (below.Length == 0) return new Location(root.FullName, "");

        if (await TopLevelAndPrefixAsync(root.FullName, ct) is not var (candidateTopLevel, candidateBelow))
            return null;

        bool isTheTopLevel = candidateBelow.Length == 0
                             && string.Equals(candidateTopLevel, topLevel, PathComparison.Comparison);
        return isTheTopLevel ? new Location(root.FullName, string.Join("/", below)) : null;
    }

    /// <summary>
    ///     <paramref name="fullPath" /> as a committed file records it: git's names for the directories below the work
    ///     tree's root, and the leaf as its directory lists it, letter case included. A path outside a work tree, or one
    ///     whose directory does not exist, keeps its own spelling.
    /// </summary>
    public static async Task<string> CommittedSpellingAsync(string fullPath, CancellationToken ct)
    {
        if (Directory.Exists(fullPath)) return (await LocateAsync(fullPath, ct))?.Directory ?? fullPath;

        string directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory)) return fullPath;

        string spelled = (await LocateAsync(directory, ct))?.Directory ?? directory;
        return Path.Join(spelled, NameOnDisk(directory, Path.GetFileName(fullPath)));
    }

    /// <summary>
    ///     <paramref name="fileName" /> as <paramref name="directory" /> lists it. Only a case-insensitive file system
    ///     lists it in another case, and an exact match wins where a case-sensitive one holds several. A name carrying
    ///     a wildcard keeps its own spelling: the listing treats it as a pattern, and a match is another file.
    /// </summary>
    public static string NameOnDisk(string directory, string fileName)
    {
        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, AttributesToSkip = 0 };
        string[] listed = Directory.GetFiles(directory, fileName, options)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (listed.Contains(fileName, StringComparer.Ordinal)) return fileName;

        return listed is [var onDisk] ? onDisk : fileName;
    }

    private static async Task<(string TopLevel, string[] Below)?> TopLevelAndPrefixAsync(
        string directory, CancellationToken ct)
    {
        string[]? lines =
            await GitProcess.AnswerLinesAsync(directory, ct, "rev-parse", "--show-toplevel", "--show-prefix");
        if (lines is not { Length: >= 2 }) return null;

        string[] below = lines[1].Split('/', StringSplitOptions.RemoveEmptyEntries);
        return (lines[0], below);
    }

    /// <summary>A directory's place in a work tree.</summary>
    /// <param name="Root">The work tree's root, spelled as the caller spelled the directory.</param>
    /// <param name="Below">
    ///     The path from the root down to the directory as git spells it, with forward slashes, and empty at the root.
    /// </param>
    public sealed record Location(string Root, string Below)
    {
        /// <summary>The directory, spelled as <see cref="Root" /> and then git's names below it.</summary>
        public string Directory =>
            Below.Length == 0 ? Root : Path.Join(Root, Below.Replace('/', Path.DirectorySeparatorChar));
    }
}
