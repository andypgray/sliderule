using Microsoft.CodeAnalysis;
using SlideRule.Internal;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Caching;

/// <summary>
///     Reduces a loaded <see cref="Solution" /> to the per-project identity both structure-keyed caches
///     fingerprint: one <see cref="ProjectInputs" /> per C# project, keyed by name, in ordinal name order.
/// </summary>
/// <remarks>
///     <para>
///         One walk serves both structure-keyed stores, so they collapse a multi-targeted project's several
///         <see cref="Project" />s onto the one name
///         <see cref="SolutionExtensions.NormalizeProjectNames">the load boundary normalized them to</see> the
///         same way: a project the two collapsed differently would be a cached model served against inputs the
///         other never watched. The stores differ only in which documents they track, which
///         <c>includeDocument</c> carries.
///     </para>
///     <para>
///         The project file, its directory, and the evaluated output pair come from whichever framework was seen
///         first: the intermediate root is derived from the prefix that pair shares, and that root is the same
///         whichever framework's pair it starts from. Documents union across frameworks into an ordinal-sorted
///         set, so a change under any one framework still dirties the single entry, and the ordering is stable
///         for a fingerprint that must not move when the workspace enumerates differently.
///     </para>
/// </remarks>
internal static class SolutionProjects
{
    /// <summary>
    ///     Collects one <see cref="ProjectInputs" /> per C# project of <paramref name="solution" />, loaded or
    ///     replayed, in ordinal name order. <paramref name="includeDocument" /> decides whether to track a
    ///     document, given the directory of the project it was found under and the document's absolute path; it
    ///     is asked per <see cref="Project" />, so a multi-targeted project answers it against the framework the
    ///     document came from. <paramref name="compilerAssemblies" /> is the compiler assembly a replay read off each
    ///     project's recorded call, which outranks the one found from the load.
    /// </summary>
    internal static IReadOnlyList<ProjectInputs> Collect(
        Solution solution, Func<string, string, bool> includeDocument,
        IReadOnlyDictionary<ProjectId, string>? compilerAssemblies = null)
    {
        var byName = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        string? solutionSdk = CompilerLagFailures.LoadSdkOf(solution);

        foreach (Project project in solution.Projects)
        {
            if (!ProjectLanguages.IsCSharp(project) || project.FilePath is null) continue;

            string csprojPath = Path.GetFullPath(project.FilePath);
            string projectDirectory = Path.GetDirectoryName(csprojPath)!;

            if (!byName.TryGetValue(project.Name, out Accumulator? accumulator))
            {
                accumulator = new Accumulator(
                    project.Name,
                    csprojPath,
                    projectDirectory,
                    project.OutputFilePath,
                    project.CompilationOutputInfo.AssemblyPath);
                byName[project.Name] = accumulator;
            }

            // Every framework's, because a change to any one of them can make the carried compiler lag that framework.
            string? compiler = compilerAssemblies?.GetValueOrDefault(project.Id)
                               ?? CompilerLagFailures.YardstickOf(project, null, solutionSdk);
            if (compiler is not null) accumulator.Compilers.Add(compiler);

            AddDocuments(project, projectDirectory, includeDocument, accumulator.Documents);

            foreach (string referenceName in solution.ReferencedProjectNames(project))
                accumulator.References.Add(referenceName);
        }

        return byName.Values
            .OrderBy(accumulator => accumulator.ProjectName, StringComparer.Ordinal)
            .Select(accumulator => accumulator.ToInputs())
            .ToList();
    }

    /// <summary>
    ///     Collects one <see cref="ForeignProjectInputs" /> per loaded project in another language that has a project
    ///     file, keyed by that file and in its ordinal order, with its documents unioned across frameworks and filtered
    ///     by <paramref name="includeDocument" /> as <see cref="Collect" /> filters a C# project's.
    /// </summary>
    internal static IReadOnlyList<ForeignProjectInputs> CollectForeign(
        Solution solution, Func<string, string, bool> includeDocument)
    {
        var byFile = new Dictionary<string, (Project First, SortedSet<string> Documents)>(PathComparison.Comparer);

        foreach (Project project in solution.Projects)
        {
            if (ProjectLanguages.IsCSharp(project) || project.FilePath is null) continue;

            string projectFile = Path.GetFullPath(project.FilePath);
            string projectDirectory = Path.GetDirectoryName(projectFile)!;
            if (!byFile.TryGetValue(projectFile, out (Project First, SortedSet<string> Documents) seen))
            {
                seen = (project, new SortedSet<string>(StringComparer.Ordinal));
                byFile[projectFile] = seen;
            }

            AddDocuments(project, projectDirectory, includeDocument, seen.Documents);
        }

        return byFile
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ForeignProjectInputs(
                pair.Key,
                Path.GetDirectoryName(pair.Key)!,
                ProjectLanguages.SourceSuffixOf(pair.Value.First),
                pair.Value.Documents.ToList(),
                pair.Value.First.OutputFilePath,
                pair.Value.First.CompilationOutputInfo.AssemblyPath))
            .ToList();
    }

    // Adds the project's documents includeDocument keeps, as absolute paths.
    private static void AddDocuments(
        Project project, string projectDirectory, Func<string, string, bool> includeDocument, ISet<string> documents)
    {
        foreach (Document document in project.Documents)
        {
            if (document.FilePath is null) continue;

            string documentPath = Path.GetFullPath(document.FilePath);
            if (includeDocument(projectDirectory, documentPath)) documents.Add(documentPath);
        }
    }

    // Accumulates one project's identity and its unioned, ordinal-sorted document, reference and compiler sets across
    // frameworks.
    private sealed class Accumulator(
        string projectName,
        string csprojPath,
        string projectDirectory,
        string? evaluatedOutputPath,
        string? intermediateAssemblyPath)
    {
        public string ProjectName { get; } = projectName;
        public SortedSet<string> Documents { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> References { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Compilers { get; } = new(StringComparer.Ordinal);

        public ProjectInputs ToInputs()
        {
            return new ProjectInputs(
                ProjectName,
                csprojPath,
                projectDirectory,
                References.ToList(),
                Documents.ToList(),
                evaluatedOutputPath,
                intermediateAssemblyPath,
                Compilers.ToList());
        }
    }
}
