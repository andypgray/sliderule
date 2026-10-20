using SlideRule.Codebase;

namespace SlideRule.ModelFile;

/// <summary>One project of the checked universe, and the assemblies it produces.</summary>
/// <remarks>
///     The assembly names are what let a reader with no project system to ask decide whether a compilation is
///     one the file speaks about.
/// </remarks>
internal sealed class ModelDocumentProject
{
    internal ModelDocumentProject(string name, IReadOnlyList<string> assemblies)
    {
        Name = name;
        Assemblies = assemblies;
    }

    internal string Name { get; }

    /// <summary>
    ///     The assembly names the project produces, ordinally sorted and distinct as the caller supplies them;
    ///     the composer writes them in the order given.
    /// </summary>
    internal IReadOnlyList<string> Assemblies { get; }

    /// <summary>
    ///     The one mapping from a codebase's projects, so the writer of the file and every gate that recomposes
    ///     it agree about what a project is. A codebase built from bare compilations states no assembly name and
    ///     yields empty lists.
    /// </summary>
    internal static IReadOnlyList<ModelDocumentProject> ListOf(CodebaseModel codebase)
    {
        return codebase.Projects
            .Select(project => new ModelDocumentProject(project.Name, project.AssemblyNames))
            .ToList();
    }
}
