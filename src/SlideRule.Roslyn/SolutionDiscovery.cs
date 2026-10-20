using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Hosting;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn;

/// <summary>
///     Resolves which solution file to load, by the precedence <see cref="Choose" /> states, and returns it absolute
///     and symlink-free (<see cref="PathCanonicalizer" />), so it compares equal to a path another tool resolved,
///     such as git's toplevel.
/// </summary>
/// <remarks>
///     A search climbs from its start to the first directory holding exactly one <c>.sln</c>, <c>.slnx</c> or
///     <c>.slnf</c>, where a <c>.slnf</c> counts only when no full solution stands beside it. Nothing is guessed: a
///     directory holding several solutions is climbed past rather than chosen from, and a search that reaches the
///     root without finding a directory that holds exactly one throws rather than picking one.
/// </remarks>
// Both refusals must keep naming the solution argument before the environment variable, and the
// "nothing anywhere" one must keep naming any solution one level down; the pinned message texts are
// what hold that.
internal static class SolutionDiscovery
{
    /// <summary>How many near misses the "nothing anywhere" refusal lists before the "and N more" tail.</summary>
    private const int MaxNearMisses = 5;

    // Build output and tooling directories: a solution under one of these is an artefact or a checkout of
    // something else, never the repository's own, and naming it would send the reader somewhere wrong.
    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git", ".vs", "node_modules", "packages"];

    // Both refusals end here. No runner is named: the line is also read from MCP sessions a registry manifest
    // launched, which have no `sliderule` command to put in front of the verb.
    private static string FixLine =>
        "Pass the solution as the argument (<command> <solution>, or in your MCP client "
        + $"config's args), or set {SlideRuleEnvVars.SolutionPath} to it.";

    /// <summary>
    ///     Discovers the solution file to load from <paramref name="argument" />,
    ///     <paramref name="environmentPath" /> (the caller's reading of <c>SLIDERULE_SOLUTION_PATH</c>) and
    ///     <paramref name="workingDirectory" />, an absolute path, by the precedence <see cref="Choose" /> states.
    /// </summary>
    /// <exception cref="FileNotFoundException">A solution file was named but no file exists there.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The search reached the root without finding a directory that holds exactly one solution.
    /// </exception>
    public static string DiscoverSolution(string? argument, string workingDirectory, string? environmentPath)
    {
        SolutionStart start = Choose(argument, workingDirectory, environmentPath);
        if (start.Kind == SolutionStartKind.Search) return Search(start.Path);

        string resolved = PathCanonicalizer.Resolve(start.Path);
        if (File.Exists(resolved)) return resolved;

        string message = start.Kind == SolutionStartKind.VariableFile
            ? $"{SlideRuleEnvVars.SolutionPath} points to a file that does not exist: {resolved}"
            : $"Solution file not found: {resolved}";
        throw new FileNotFoundException(message, resolved);
    }

    /// <summary>
    ///     Where discovery starts, and the one statement of its precedence: a non-blank <paramref name="argument" />
    ///     (a directory to search from, otherwise a solution file), then a non-blank
    ///     <paramref name="environmentPath" /> (a solution file), then a search from
    ///     <paramref name="workingDirectory" />. Every relative path resolves against <paramref name="workingDirectory" />.
    ///     Nothing is checked for existence beyond telling a directory argument from a file one.
    /// </summary>
    // The settings resolver switches on this too, so a run's settings and its solution come from the same start.
    internal static SolutionStart Choose(string? argument, string workingDirectory, string? environmentPath)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            string full = Path.GetFullPath(argument, workingDirectory);
            return Directory.Exists(full)
                ? new SolutionStart(full, SolutionStartKind.Search)
                : new SolutionStart(full, SolutionStartKind.ArgumentFile);
        }

        if (!string.IsNullOrWhiteSpace(environmentPath))
            return new SolutionStart(Path.GetFullPath(environmentPath, workingDirectory), SolutionStartKind.VariableFile);

        return new SolutionStart(workingDirectory, SolutionStartKind.Search);
    }

    /// <summary>
    ///     <paramref name="start" /> and each directory above it, nearest first, with the solution files each holds
    ///     (<see cref="FindSlnFiles" />). The settings resolver walks the same ancestors, so a change here moves
    ///     where settings are found too.
    /// </summary>
    internal static IEnumerable<(string Directory, string[] Solutions)> Ancestors(string start)
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            yield return (directory.FullName, FindSlnFiles(directory.FullName));
    }

    private static string Search(string start)
    {
        (string Directory, string[] Solutions)? firstAmbiguous = null;
        foreach ((string directory, string[] solutions) in Ancestors(start))
        {
            if (solutions.Length == 1) return PathCanonicalizer.Resolve(solutions[0]);

            if (solutions.Length > 1 && firstAmbiguous is null) firstAmbiguous = (directory, solutions);
        }

        if (firstAmbiguous is { } ambiguous)
            throw new InvalidOperationException(AmbiguousMessage(ambiguous.Directory, ambiguous.Solutions));

        throw new InvalidOperationException(NotFoundMessage(start, NearMisses(start)));
    }

    /// <summary>
    ///     The refusal when no ancestor holds exactly one solution and <paramref name="directory" />, the nearest
    ///     that held several, held <paramref name="candidates" />. Pure, and internal so the text pins without a
    ///     filesystem.
    /// </summary>
    internal static string AmbiguousMessage(string directory, IReadOnlyList<string> candidates)
    {
        List<string> names = candidates
            .Select(candidate => Path.GetFileName(candidate))
            .ToList();

        return EvidenceBlock.Compose($"Multiple solution files found in '{directory}':", names, FixLine);
    }

    /// <summary>
    ///     The refusal when the whole walk-up from <paramref name="directory" /> found nothing, naming any solution
    ///     one level below it (<paramref name="nearMisses" />, absolute, rendered relative to
    ///     <paramref name="directory" />) so a repository whose solution sits under <c>src\</c> is one copy-paste
    ///     from working rather than a dead end.
    /// </summary>
    internal static string NotFoundMessage(string directory, IReadOnlyList<string> nearMisses)
    {
        var header = $"No .sln, .slnf or .slnx file found in '{directory}' or any parent directory.";

        // The heading rides the lede rather than standing as its own line: a walk-up that saw nothing one
        // level down must not announce a list it is about to leave empty.
        string lede = nearMisses.Count > 0
            ? header + "\nSolution files one level down:"
            : header;

        List<string> relative = nearMisses
            .Select(nearMiss => Path.GetRelativePath(directory, nearMiss))
            .ToList();

        return EvidenceBlock.Compose(lede, relative, FixLine, quoteCap: MaxNearMisses);
    }

    /// <summary>
    ///     Solutions in the immediate subdirectories of <paramref name="startDirectory" />, ordered, for the
    ///     refusal to name: discovery reports what it saw and still refuses, never widening the search into a
    ///     guess. An unreadable directory yields nothing rather than replacing the refusal with an I/O error.
    /// </summary>
    internal static IReadOnlyList<string> NearMisses(string startDirectory)
    {
        try
        {
            return Directory.EnumerateDirectories(startDirectory)
                .Where(directory => !SkippedDirectories.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase))
                .OrderBy(directory => directory, StringComparer.Ordinal)
                .SelectMany(FindSlnFiles)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    ///     The solution files in one directory, ordered, less any <c>.slnf</c> standing beside a <c>.sln</c> or
    ///     <c>.slnx</c>: a filter is a view of a solution, so the two together is a normal layout rather than a
    ///     question.
    /// </summary>
    private static string[] FindSlnFiles(string directory)
    {
        string[] candidates = Directory.EnumerateFiles(directory)
            .Where(SolutionProjectFileParser.OwnsFormat)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] fullSolutions = candidates
            .Where(path => !SolutionProjectFileParser.IsFilterFormat(path))
            .ToArray();

        return fullSolutions.Length > 0 ? fullSolutions : candidates;
    }
}
