using SlideRule.Internal;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     Turns the project files a project's evaluation declared references to into the names of the loaded
///     projects they are. Split into a map over the loaded projects and a lookup through it, both taking the
///     canonicalizer as a delegate, so the pair is unit-testable over synthetic paths with no workspace and no
///     disk.
/// </summary>
/// <remarks>
///     Both sides of the lookup are canonicalized, because the two spellings of one project file come from
///     different readers: the workspace reports a project's path as the solution named it, while an
///     evaluated reference is its include resolved against the referencing project's directory. A link on
///     either route would otherwise leave a declared reference unnamed.
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
}
