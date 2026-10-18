namespace SlideRule;

/// <summary>
///     One MSBuild property a declaration in the solution set for a project, as a project predicate reads it
///     from <see cref="IProjectInfo.Properties" />: the property's name, its evaluated value, the target
///     framework whose build evaluated it, and where the definition that won is declared.
/// </summary>
// Read by the predicate authors this contract exists for rather than through the interface in-solution, so
// solution-wide search finds only the implementation and calls the members unused.
// ReSharper disable UnusedMemberInSuper.Global
public interface IProjectPropertyInfo
{
    /// <summary>The property's name, as its definition spells it. MSBuild matches property names ignoring case.</summary>
    string Name { get; }

    /// <summary>
    ///     The value the build evaluated, with every property it refers to expanded, such as <c>enable</c> for
    ///     <c>Nullable</c>. A definition that sets the property to nothing reads as the empty string.
    /// </summary>
    string Value { get; }

    /// <summary>
    ///     The target framework, as a short moniker, whose build evaluated this value, so a property set only
    ///     under a <c>$(TargetFramework)</c> condition appears once for each framework it holds in. Null where
    ///     the build was evaluated with no framework told apart: a project declaring several frameworks that was
    ///     evaluated once.
    /// </summary>
    string? TargetFramework { get; }

    /// <summary>
    ///     The file that declares the definition that won, as an absolute path: the project file, a
    ///     <c>Directory.Build.props</c> above it, or a props file the project imports from inside the solution.
    /// </summary>
    string FilePath { get; }

    /// <summary>The 1-based line of that definition in <see cref="FilePath" />.</summary>
    int Line { get; }
}
