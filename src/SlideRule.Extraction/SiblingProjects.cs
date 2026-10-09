using SlideRule.Extraction.Fragments;

namespace SlideRule.Extraction;

/// <summary>
///     Which project of the solution produces each assembly, as one compilation's extraction reads it:
///     the answer to "the compilation bound this type from assembly X — whose project is that, and can
///     this view say?".
/// </summary>
/// <remarks>
///     <para>
///         Three cases, and the one-project extraction turns on exactly them: an assembly no project here
///         produces is an ordinary external; an assembly <em>one</em> project produces is a sibling and
///         that is its project name; an assembly <em>several</em> produce cannot be attributed from this
///         view at all, and its types are dropped rather than guessed at.
///     </para>
///     <para>
///         Assembly names compare ordinally, through the <see cref="AssemblyProducers" /> index
///         <see cref="FragmentMerger" /> also builds over the fragments, and ordinal is the comparison the
///         merge makes between a fragment's own assembly name and the one an external was bound from: a view
///         that told two assemblies apart differently from the merge would attribute a type the merge then
///         shadows.
///     </para>
///     <para>
///         The own project is answered as if it were absent, so a caller may hand over the whole table it
///         read rather than a copy with one row removed — but it still counts towards ambiguity, because an
///         assembly name this compilation shares with a sibling says nothing about whose type a reference
///         reached.
///     </para>
/// </remarks>
internal sealed class SiblingProjects
{
    private readonly AssemblyProducers _projectsByAssembly = new();

    private readonly string _ownProjectName;

    /// <summary>
    ///     Builds the map from the solution's projects and the assemblies each produces.
    /// </summary>
    /// <param name="projects">
    ///     One entry per project, each naming the assemblies it produces — a multi-targeted project
    ///     normally naming one.
    /// </param>
    /// <param name="ownProjectName">
    ///     The project whose compilation is being extracted, which is never its own sibling.
    /// </param>
    internal SiblingProjects(
        IReadOnlyList<(string ProjectName, IReadOnlyList<string> AssemblyNames)> projects, string ownProjectName)
    {
        _ownProjectName = ownProjectName;

        foreach ((string projectName, IReadOnlyList<string> assemblyNames) in projects)
        foreach (string assemblyName in assemblyNames)
            _projectsByAssembly.Record(assemblyName, projectName);
    }

    /// <summary>
    ///     The sibling project producing <paramref name="assemblyName" />, or <see langword="false" /> for
    ///     an assembly this view cannot attribute to one sibling: unknown here, produced by several
    ///     projects, or produced by the project being extracted.
    /// </summary>
    internal bool TryGetProject(string assemblyName, out string? projectName)
    {
        if (!_projectsByAssembly.TryGetProducer(assemblyName, out projectName)) return false;
        if (!string.Equals(projectName, _ownProjectName, StringComparison.Ordinal)) return true;

        projectName = null;
        return false;
    }

    /// <summary>
    ///     Whether several projects produce <paramref name="assemblyName" />, which is what makes a type
    ///     in it unattributable from this view rather than merely foreign.
    /// </summary>
    internal bool IsAmbiguous(string assemblyName)
    {
        return _projectsByAssembly.IsAmbiguous(assemblyName);
    }
}
