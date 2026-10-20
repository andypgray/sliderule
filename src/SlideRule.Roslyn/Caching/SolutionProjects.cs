using Microsoft.CodeAnalysis;
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
    ///     document came from.
    /// </summary>
    internal static IReadOnlyList<ProjectInputs> Collect(
        Solution solution, Func<string, string, bool> includeDocument)
    {
        var byName = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        foreach (Project project in solution.Projects)
        {
            if (project.Language != LanguageNames.CSharp || project.FilePath is null) continue;

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

            foreach (Document document in project.Documents)
            {
                if (document.FilePath is null) continue;

                string documentPath = Path.GetFullPath(document.FilePath);
                if (includeDocument(projectDirectory, documentPath)) accumulator.Documents.Add(documentPath);
            }

            foreach (ProjectReference reference in project.ProjectReferences)
                if (solution.GetProject(reference.ProjectId)?.Name is { } referenceName)
                    accumulator.References.Add(referenceName);
        }

        return byName.Values
            .OrderBy(accumulator => accumulator.ProjectName, StringComparer.Ordinal)
            .Select(accumulator => accumulator.ToInputs())
            .ToList();
    }

    // Accumulates one project's identity and its unioned, ordinal-sorted document and reference sets across
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

        public ProjectInputs ToInputs()
        {
            return new ProjectInputs(
                ProjectName,
                csprojPath,
                projectDirectory,
                References.ToList(),
                Documents.ToList(),
                evaluatedOutputPath,
                intermediateAssemblyPath);
        }
    }
}
