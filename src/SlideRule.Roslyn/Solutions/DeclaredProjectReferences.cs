using SlideRule.Internal;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     Turns the project files a project's evaluation declared references to into the names of the loaded
///     projects they are, and names the workspace references those declarations do not account for.
/// </summary>
/// <remarks>
///     <para>
///         The canonicalizer and a declared project's reach arrive as delegates, so all of it is unit-testable
///         over synthetic input with no workspace and no disk.
///     </para>
///     <para>
///         Both sides of the lookup are canonicalized, because the two spellings of one project file come from
///         different readers: the workspace reports a project's path as the solution named it, while an
///         evaluated reference is its include resolved against the referencing project's directory. A link on
///         either route would otherwise leave a declared reference unnamed.
///     </para>
/// </remarks>
internal static class DeclaredProjectReferences
{
    /// <summary>
    ///     Each loaded project's name by its canonical project file. A multi-targeted project is loaded once
    ///     per framework under one name, so the first entry for a file stands and the rest change nothing; a
    ///     project the workspace reported no file for cannot be referred to by one, and is skipped.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> NamesByProjectFile(
        IEnumerable<(string? FilePath, string Name)> projects, Func<string?, string?> canonicalize)
    {
        var namesByProjectFile = new Dictionary<string, string>(PathComparison.Comparer);
        foreach ((string? filePath, string name) in projects)
            if (canonicalize(filePath) is { } canonical)
                namesByProjectFile.TryAdd(canonical, name);

        return namesByProjectFile;
    }

    /// <summary>
    ///     The names of the loaded projects <paramref name="declaredPaths" /> point at, distinct and in ordinal
    ///     order. A path no loaded project answers to is dropped, as the workspace drops a reference to a
    ///     project it did not load.
    /// </summary>
    internal static IReadOnlyList<string> Names(
        IEnumerable<string> declaredPaths,
        IReadOnlyDictionary<string, string> namesByProjectFile,
        Func<string?, string?> canonicalize)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string declaredPath in declaredPaths)
            if (canonicalize(declaredPath) is { } canonical && namesByProjectFile.TryGetValue(canonical, out string? name))
                names.Add(name);

        return names.ToList();
    }

    /// <summary>
    ///     The projects in <paramref name="workspaceReferences" /> that <paramref name="declared" /> does not
    ///     account for, distinct and in ordinal order. A reference is accounted for when it is declared, or when a
    ///     declared project reaches it through <paramref name="reach" />, which is how the SDK's transitive
    ///     references arrive.
    /// </summary>
    // The workspace's list is what the compilation was built against, and every name on it comes from some
    // declaration somewhere in the project graph. A name nothing declared reaches is a declaration this
    // evaluation did not see, which is what an include spelled with a property the evaluation lacks looks like.
    internal static IReadOnlyList<string> Undeclared(
        IEnumerable<string> workspaceReferences,
        IReadOnlyList<string> declared,
        Func<string, IEnumerable<string>> reach)
    {
        var accounted = new HashSet<string>(declared, StringComparer.Ordinal);
        foreach (string name in declared) accounted.UnionWith(reach(name));

        return workspaceReferences
            .Where(reference => !accounted.Contains(reference))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToList();
    }
}
