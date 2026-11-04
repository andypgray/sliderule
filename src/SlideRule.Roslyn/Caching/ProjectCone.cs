using System.IO.Enumeration;
using SlideRule.Internal;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Caching;

/// <summary>
///     The file-system probes that decide, with zero MSBuild, whether a tree's on-disk shape has moved: the
///     <see cref="Enumerate">cone</see> of <c>*.cs</c> a project directory globs, and the
///     <see cref="SolutionStructuralPaths(string, IEnumerable{ProjectInputs})">structural path set</see> a whole
///     solution stamps.
/// </summary>
/// <remarks>
///     Every caller deciding whether a model is still valid asks these, so none can drift from another on what
///     counts as a source file or how far the probe chain reaches: a path one stamps and another does not is a
///     stale answer served confidently, with nothing red. Every yielded path is a canonical full path, so a
///     caller keys the results against a set built with <see cref="PathComparison.Comparer" /> and compares like
///     for like.
/// </remarks>
internal static class ProjectCone
{
    // Compatible-mode enumeration, matched deliberately to the DirectoryInfo walk this scan is defined
    // against: hidden and system entries are kept (the framework's own default skips both) and an
    // inaccessible directory throws rather than yielding nothing. Either default would shrink the
    // cone silently, and a file the cone never sees is an add that is never detected — a stale model served
    // confidently, which is the one failure these probes exist to prevent.
    private static readonly EnumerationOptions ConeEnumeration = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.None,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false
    };

    private static readonly EnumerationOptions ConeEnumerationSkippingInaccessible = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.None,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false
    };

    /// <summary>
    ///     Enumerates every <c>*.cs</c> under <paramref name="projectDirectory" /> (recursively), skipping the
    ///     <c>bin</c>/<c>obj</c> build-output subtrees; a non-existent directory yields nothing. This is the SDK
    ///     default-glob cone, the source-membership set a stat sweep cannot see change, because an SDK-glob add
    ///     touches no MSBuild file. The cone is a superset of a project's <em>compiled</em> documents: a
    ///     <c>&lt;Compile Remove&gt;</c>'d or <c>None</c>-typed <c>*.cs</c> still lives here, so callers must record
    ///     cone membership to tell a genuine add from a file that was never compiled in the first place.
    ///     <paramref name="skipInaccessible" /> skips a directory the walk cannot open instead of throwing, for a
    ///     caller whose answer is advisory; a caller that must never serve a stale answer keeps the throw.
    /// </summary>
    /// <remarks>
    ///     The walk streams, so a caller that asks only whether the cone holds anything new stops at the first
    ///     hit. The pruned directory names are ordinal, so a differently-cased <c>BIN</c> is walked, matching
    ///     <see cref="BuildOutputDirectories.IsUnderBuildOutput" />; the <c>.cs</c> suffix follows
    ///     <see cref="PathComparison" />, as the build's glob matches file names on each platform, so a <c>.CS</c> is
    ///     source on Windows. A directory link is followed, as the build follows it, unless it leads back into the
    ///     walk or to no directory (<see cref="IsUnwalkableLink" />). Order is whatever the file system yields:
    ///     <see cref="Adds" /> sorts, and no caller may key on the raw order.
    /// </remarks>
    public static IEnumerable<string> Enumerate(string projectDirectory, bool skipInaccessible = false)
    {
        string root = Path.GetFullPath(projectDirectory);
        if (!Directory.Exists(root)) return [];

        return new FileSystemEnumerable<string>(
            root,
            static (ref entry) => entry.ToFullPath(),
            skipInaccessible ? ConeEnumerationSkippingInaccessible : ConeEnumeration)
        {
            ShouldIncludePredicate = static (ref entry) =>
                !entry.IsDirectory && entry.FileName.EndsWith(".cs", PathComparison.Comparison),
            ShouldRecursePredicate = (ref entry) =>
                !BuildOutputDirectories.IsBuildOutputName(entry.FileName)
                && !((entry.Attributes & FileAttributes.ReparsePoint) != 0
                     && IsUnwalkableLink(root, entry.Directory.ToString(), entry.ToFullPath()))
        };
    }

    // Whether the directory link at linkPath must not be entered: it resolves to no directory (a dangling link, or
    // links that point at each other, which throw when entered), or it leads to a directory already on the walk's
    // way down to it, the shape of every link cycle, which never ends. The descent is compared by real paths,
    // because a link reached through another link never contains its own target lexically. A reparse point that
    // is not a link is entered like any directory.
    private static bool IsUnwalkableLink(string root, string parentDirectory, string linkPath)
    {
        string? resolved;
        try
        {
            resolved = new DirectoryInfo(linkPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }

        if (resolved is null) return false;
        if (!Directory.Exists(resolved)) return true;

        string target = PathCanonicalizer.Resolve(resolved);
        string top = Path.TrimEndingDirectorySeparator(root);
        for (string? directory = parentDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (IsAncestorOrSelf(target, PathCanonicalizer.Resolve(directory))) return true;
            if (PathComparison.Comparer.Equals(directory, top)) break;
        }

        return false;
    }

    // Compared with a separator closing both, so a drive root ("C:\") needs no case of its own and "C:\work2" is
    // not under "C:\work".
    private static bool IsAncestorOrSelf(string ancestor, string path)
    {
        string prefix = Path.EndsInDirectorySeparator(ancestor) ? ancestor : ancestor + Path.DirectorySeparatorChar;
        string probe = Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
        return probe.StartsWith(prefix, PathComparison.Comparison);
    }

    /// <summary>
    ///     The cone's <c>*.cs</c> that <paramref name="known" /> does not already hold, ordinal-sorted so a caller
    ///     keying on the result gets the same answer whatever order the file system enumerated.
    /// </summary>
    public static IReadOnlyList<string> Adds(string projectDirectory, ICollection<string> known)
    {
        List<string> adds = Enumerate(projectDirectory)
            .Where(full => !known.Contains(full))
            .ToList();
        adds.Sort(StringComparer.Ordinal);
        return adds;
    }

    /// <summary>
    ///     The first cone <c>*.cs</c> that <paramref name="known" /> does not hold, or null when there is
    ///     none — the same question <see cref="Adds" /> answers, for a caller that needs only a witness.
    /// </summary>
    /// <remarks>
    ///     "First" is first-enumerated rather than first-ordinal: the two differ only in <em>which</em> new file a
    ///     notice names when several appeared at once, and any caller that needs the stable choice is asking
    ///     <see cref="Adds" />.
    /// </remarks>
    public static string? FirstAdd(string projectDirectory, ICollection<string> known)
    {
        return Enumerate(projectDirectory)
            .FirstOrDefault(full => !known.Contains(full));
    }

    /// <summary>
    ///     Every non-source file whose state changes what a build of the project in
    ///     <paramref name="projectDirectory" /> produces: every place the layout in force could put its
    ///     <see cref="IntermediateOutputTree.AssetsPathsOf">restore assets file</see>, then each ancestor
    ///     directory crossed with
    ///     <see cref="FileStamping.StructuralProbeFileNames">every probe name</see> — absent ones included,
    ///     because a probe that later appears is itself the change.
    /// </summary>
    /// <remarks>
    ///     The order is the order stamps are recorded in, so a persisted stamp list keeps its shape. This is the
    ///     per-project course of <see cref="SolutionStructuralPaths(string, IEnumerable{ProjectInputs})" />.
    /// </remarks>
    public static IEnumerable<string> StructuralPaths(
        string projectDirectory, string? evaluatedOutputPath, string? intermediateAssemblyPath)
    {
        foreach (string assets in
                 IntermediateOutputTree.AssetsPathsOf(projectDirectory, evaluatedOutputPath, intermediateAssemblyPath))
            yield return assets;

        foreach (string ancestor in Ancestors(projectDirectory))
        foreach (string probe in FileStamping.StructuralProbeFileNames)
            yield return Path.Combine(ancestor, probe);
    }

    /// <summary>
    ///     One solution's whole structural set, deduplicated and in stamping order: the solution (or filter) file
    ///     at <paramref name="solutionPath" />, the solution a filter points at, then each of
    ///     <paramref name="projects" />, in the order given, as its file followed by its
    ///     <see cref="StructuralPaths" />.
    /// </summary>
    /// <remarks>
    ///     The filter leg costs a non-filter solution nothing: the format test comes before any I/O.
    /// </remarks>
    public static IReadOnlyList<string> SolutionStructuralPaths(
        string solutionPath, IEnumerable<ProjectInputs> projects)
    {
        return SolutionStructuralPaths(
            solutionPath,
            projects.Select(project => (
                Path.GetFullPath(project.CsprojPath),
                Path.GetFullPath(project.ProjectDirectory),
                project.EvaluatedOutputPath,
                project.IntermediateAssemblyPath)));
    }

    /// <summary>
    ///     <see cref="SolutionStructuralPaths(string, IEnumerable{ProjectInputs})" /> over projects given by their
    ///     structural facts alone, each <c>CsprojPath</c> and <c>ProjectDirectory</c> already absolute.
    /// </summary>
    public static IReadOnlyList<string> SolutionStructuralPaths(
        string solutionPath,
        IEnumerable<(string CsprojPath, string ProjectDirectory, string? EvaluatedOutputPath, string?
            IntermediateAssemblyPath)> projects)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(PathComparison.Comparer);

        string fullSolution = Path.GetFullPath(solutionPath);
        Add(fullSolution);

        // Under a .slnf the file above is the filter, not the solution. Editing the solution changes what a
        // run loads — adding a member the filter selects, or any member at all when its projects array is
        // empty — while leaving the filter's own bytes and timestamp untouched, so without this the edit
        // never dirties the cache and the stale answer is served indefinitely.
        if (SolutionProjectFileParser.TryReadReferencedSolution(fullSolution) is { } referencedSolution)
            Add(referencedSolution);

        foreach ((string csprojPath, string projectDirectory, string? evaluatedOutputPath,
                     string? intermediateAssemblyPath) in projects)
        {
            Add(csprojPath);

            foreach (string path in StructuralPaths(projectDirectory, evaluatedOutputPath, intermediateAssemblyPath))
                Add(path);
        }

        return paths;

        void Add(string path)
        {
            if (seen.Add(path)) paths.Add(path);
        }
    }

    /// <summary>
    ///     Yields <paramref name="startDirectory" /> and each ancestor directory up to the filesystem root.
    ///     The probe chain walks the whole way up rather than stopping at the solution directory: MSBuild
    ///     honors a <c>Directory.Build.props</c>/<c>.targets</c> or <c>global.json</c> found <em>above</em> the
    ///     solution too, so a stamp chain that stopped at the solution directory would miss one and let it flip
    ///     silently. The extra levels are absent-file stamps, which are cheap.
    /// </summary>
    private static IEnumerable<string> Ancestors(string startDirectory)
    {
        string? directory = startDirectory;
        while (directory is not null)
        {
            yield return directory;
            directory = Path.GetDirectoryName(directory);
        }
    }
}
