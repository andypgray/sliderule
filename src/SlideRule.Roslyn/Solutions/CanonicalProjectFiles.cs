using Microsoft.CodeAnalysis;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     The canonical spelling of each loaded project's <c>.csproj</c>, what
///     <see cref="PathCanonicalizer.Resolve(string)" /> returns for its <see cref="Project.FilePath" />, resolved once
///     at the load boundary so that every walk over the load compares the same strings without probing the
///     filesystem again.
/// </summary>
/// <remarks>
///     Keyed by <see cref="ProjectId" />, which survives <c>Solution.WithDocumentText</c>, so it holds for the whole
///     load generation, as the target-framework map does, and a reload builds a new one. A project the load reported
///     no file for is absent, and answers null.
/// </remarks>
internal sealed class CanonicalProjectFiles
{
    private readonly Dictionary<ProjectId, string> pathsByProject;

    private CanonicalProjectFiles(Dictionary<ProjectId, string> pathsByProject)
    {
        this.pathsByProject = pathsByProject;
    }

    /// <summary>Resolves the project files of every project in <paramref name="solution" />.</summary>
    internal static CanonicalProjectFiles Of(Solution solution)
    {
        var canonicalizer = new ProjectFileCanonicalizer();
        var pathsByProject = new Dictionary<ProjectId, string>();
        foreach (Project project in solution.Projects)
            if (canonicalizer.Resolve(project.FilePath) is { } canonical)
                pathsByProject[project.Id] = canonical;

        return new CanonicalProjectFiles(pathsByProject);
    }

    /// <summary>The canonical <c>.csproj</c> path of <paramref name="project" />, or null when it has none.</summary>
    internal string? PathOf(Project project)
    {
        return pathsByProject.GetValueOrDefault(project.Id);
    }
}
