using Microsoft.CodeAnalysis;
using SlideRule.Internal;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     What one load produced, in the shapes a verdict has to distinguish: the projects that
///     <see cref="Failed" />, which gates; the declared members a solution filter left <see cref="Unchecked" />,
///     which narrows the verdict without invalidating it; and the <see cref="Unsupported" /> projects no load
///     could have produced, which state the run's coverage.
/// </summary>
internal sealed record ProjectLoadReport(
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Unchecked,
    IReadOnlyList<UnsupportedProject> Unsupported);

/// <summary>
///     Which projects failed to load, answered from the structure of the loaded <see cref="Solution" /> rather
///     than from the text of any MSBuild message.
/// </summary>
/// <remarks>
///     <para>
///         Both arms are structural. Arm 1: a <c>.csproj</c> the solution file declares that produced no
///         <see cref="Project" />. Arm 2: a <see cref="Project" /> carrying neither an evaluated
///         <see cref="Project.OutputFilePath" /> nor a <see cref="CompilationOutputInfo.AssemblyPath" />, the shape
///         Roslyn leaves behind when a project's evaluation produced a real failure. A project whose NuGet packages
///         did not resolve loads completely and presents neither arm; <see cref="RestoreFailures" /> reads that
///         cause off disk.
///     </para>
///     <para>
///         A solution filter narrows arm 1 rather than disabling it: the arm reads
///         <see cref="SolutionMembership.Required" />, so a selected project that failed to load is blamed
///         like any other. <see cref="ProjectLoadReport.Unchecked" /> is the subtraction against
///         <see cref="SolutionMembership.Declared" />, never derived from filter text: Roslyn loads a filter's
///         projects plus their transitive <c>ProjectReference</c> closure, so filter text overstates the gap.
///         <see cref="ProjectLoadReport.Unsupported" /> is read off the solution file and outside both
///         subtractions: a project no extractor reaches was never owed, so a polyglot solution cannot be refused
///         for holding one.
///     </para>
/// </remarks>
internal static class ProjectLoadFailures
{
    /// <summary>
    ///     What the load did and did not produce. The failed and unchecked lists are absolute <c>.csproj</c> paths,
    ///     ordinal-sorted and deduplicated by this OS's path rule.
    /// </summary>
    /// <remarks>
    ///     <paramref name="solutionPath" /> is the solution file the load came from, or the one a replayed build was
    ///     run over, or null when none is known; arm 1 needs one in a format the parser owns and is skipped without
    ///     it, and arm 2 runs either way. <paramref name="projectFiles" /> holds the loaded projects' canonical
    ///     paths, which arm 1 compares the solution's declared paths against.
    /// </remarks>
    internal static ProjectLoadReport Detect(
        Solution solution, CanonicalProjectFiles projectFiles, string? solutionPath)
    {
        var failed = new HashSet<string>(PathComparison.Comparer);

        // Arm 2 first, over what did load: a multi-target-framework project is several Projects behind one
        // file path, so the set collapses them to the one csproj a reader would go and fix. A project in
        // another language is skipped: F# reaches a loaded solution with its output paths — measured — so this
        // arm can see a project the model was never going to contain, and blaming one would refuse a healthy
        // polyglot solution as a load failure; the run states such a project under its coverage statement
        // instead.
        foreach (Project project in solution.Projects)
            if (ProjectLanguages.IsCSharp(project) && LoadedEmpty(project) && project.FilePath is { } filePath)
                failed.Add(Path.GetFullPath(filePath));

        var uncheckedMembers = new HashSet<string>(PathComparison.Comparer);
        IReadOnlyList<UnsupportedProject> unsupported = [];

        if (solutionPath is not null && SolutionProjectFileParser.OwnsFormat(solutionPath))
        {
            // Both sides canonical: a replayed project carries the path its build was run under, which need
            // not be the spelling the solution file resolves to (macOS's /var against /private/var, a junction),
            // and a mismatch here would blame a member that loaded. The reports keep the solution's spelling.
            var canonicalizer = new ProjectFileCanonicalizer();
            HashSet<string> loadedFiles = solution.Projects
                .Select(projectFiles.PathOf)
                .OfType<string>()
                .ToHashSet(PathComparison.Comparer);

            SolutionMembership membership = SolutionProjectFileParser.ReadDeclaredMembership(solutionPath);

            foreach (string required in membership.Required)
                if (!loadedFiles.Contains(canonicalizer.Resolve(required)!))
                    failed.Add(required);

            // Declared, not loaded, and not blamed — the only thing left for it to be is out of scope.
            foreach (string declared in membership.Declared)
                if (!loadedFiles.Contains(canonicalizer.Resolve(declared)!) && !failed.Contains(declared))
                    uncheckedMembers.Add(declared);

            // Sorted on the path, like every list on this report, so a document reads the same on any OS; the kind
            // orders nothing, because a reader looks these up by project.
            unsupported = membership.Unsupported
                .OrderBy(project => project.Path, StringComparer.Ordinal)
                .ToList();
        }

        return new ProjectLoadReport(Sorted(failed), Sorted(uncheckedMembers), unsupported);
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> paths)
    {
        return paths
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    // The CreateEmpty shape. Both paths are asked for rather than either, so a false positive, which would
    // refuse a healthy solution, needs two independent Roslyn facts to go missing at once. They moved
    // together in every bed measured.
    private static bool LoadedEmpty(Project project)
    {
        return project.OutputFilePath is null && project.CompilationOutputInfo.AssemblyPath is null;
    }
}
