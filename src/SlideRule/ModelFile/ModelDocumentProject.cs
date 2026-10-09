using SlideRule.Codebase;

namespace SlideRule.ModelFile;

/// <summary>One project of the checked universe, and the assemblies it produces.</summary>
/// <remarks>
///     The caller supplies the list already sorted and de-duplicated; the composer writes what it is
///     given, in the order it is given. This is what lets a reader decide whether a given compilation is
///     one the file speaks about, by assembly name, with no project system to ask.
/// </remarks>
internal sealed class ModelDocumentProject
{
    internal ModelDocumentProject(string name, IReadOnlyList<string> assemblies)
    {
        Name = name;
        Assemblies = assemblies;
    }

    /// <summary>The project's name.</summary>
    internal string Name { get; }

    /// <summary>The assembly names the project produces, ordinally sorted and distinct.</summary>
    internal IReadOnlyList<string> Assemblies { get; }

    /// <summary>
    ///     The projects <paramref name="codebase" /> declares, each under its own name and the assembly
    ///     names it compiles to.
    /// </summary>
    /// <remarks>
    ///     The two are not the same word — an <c>AssemblyName</c> property parts them — and the assembly
    ///     name is the one a reader with no project system to ask has in hand. A codebase built from bare
    ///     compilations states no assembly name and yields empty lists, which is a fact about that input.
    ///     The one mapping, so the command that writes the file and every gate that recomposes it cannot
    ///     come to disagree about what a project is.
    /// </remarks>
    internal static IReadOnlyList<ModelDocumentProject> ListOf(CodebaseModel codebase)
    {
        return codebase.Projects
            .Select(project => new ModelDocumentProject(project.Name, project.AssemblyNames))
            .ToList();
    }
}
