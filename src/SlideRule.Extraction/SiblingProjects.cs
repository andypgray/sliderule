using SlideRule.Extraction.Fragments;

namespace SlideRule.Extraction;

/// <summary>
///     Which project of the solution produces each assembly, as one compilation's extraction reads it: an
///     assembly no project here produces is an ordinary external, one that one project produces is that
///     sibling's, and one that several produce cannot be attributed from this view at all.
/// </summary>
/// <remarks>
///     <para>
///         Assembly names compare ordinally through <see cref="AssemblyProducers" />, as the merge's shadow
///         test compares a fragment's own assembly name with the one an external was bound from: a view that
///         told two assemblies apart differently would attribute a type the merge then shadows.
///     </para>
///     <para>
///         The own project is answered as if it were absent, so a caller may hand over the whole table it
///         read, but it still counts towards ambiguity: an assembly name this compilation shares with a
///         sibling says nothing about whose type a reference reached.
///     </para>
/// </remarks>
internal sealed class SiblingProjects
{
    private readonly AssemblyProducers _projectsByAssembly = new();

    private readonly string _ownProjectName;

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

    internal bool IsAmbiguous(string assemblyName)
    {
        return _projectsByAssembly.IsAmbiguous(assemblyName);
    }
}
