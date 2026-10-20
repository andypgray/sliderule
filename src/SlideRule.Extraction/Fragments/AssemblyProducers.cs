namespace SlideRule.Extraction.Fragments;

/// <summary>
///     Which project produces each assembly name: the one project where exactly one does, and no answer
///     where several do.
/// </summary>
/// <remarks>
///     Several frameworks of one project produce one assembly name and agree; two projects producing one name
///     disagree, and the entry goes null for good rather than naming either of them, because a name that does
///     not say which project it came from concludes nothing. So no answer depends on the order the pairs were
///     recorded in. <see cref="FragmentMerger" /> and <see cref="SiblingProjects" /> both fold through this one
///     index, so the merge and the one-project view attribute an assembly by the same rule.
/// </remarks>
internal sealed class AssemblyProducers
{
    private readonly Dictionary<string, string?> _projectsByAssembly = new(StringComparer.Ordinal);

    /// <summary>
    ///     Records that <paramref name="projectName" /> produces <paramref name="assemblyName" />; a null or
    ///     empty assembly name records nothing.
    /// </summary>
    internal void Record(string? assemblyName, string projectName)
    {
        if (assemblyName is not { Length: > 0 }) return;

        if (!_projectsByAssembly.TryGetValue(assemblyName, out string? existing))
            _projectsByAssembly[assemblyName] = projectName;
        else if (!string.Equals(existing, projectName, StringComparison.Ordinal))
            _projectsByAssembly[assemblyName] = null;
    }

    /// <summary>
    ///     The one project producing <paramref name="assemblyName" />, or <see langword="false" />, with
    ///     <paramref name="projectName" /> null, where none is recorded or several are.
    /// </summary>
    internal bool TryGetProducer(string assemblyName, out string? projectName)
    {
        return _projectsByAssembly.TryGetValue(assemblyName, out projectName) && projectName is not null;
    }

    /// <summary>Whether several projects produce <paramref name="assemblyName" />.</summary>
    internal bool IsAmbiguous(string assemblyName)
    {
        return _projectsByAssembly.TryGetValue(assemblyName, out string? projectName) && projectName is null;
    }
}
