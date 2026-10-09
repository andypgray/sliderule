namespace SlideRule;

/// <summary>
///     One package a project declares, as a project predicate reads it from
///     <see cref="IProjectInfo.PackageReferences" />: the package's identifier, where the reference is declared,
///     and whether it is private. The version is not recorded.
/// </summary>
// Read by the predicate authors this contract exists for rather than through the interface in-solution, so
// solution-wide search finds only the implementation and calls the members unused.
// ReSharper disable UnusedMemberInSuper.Global
public interface IPackageReferenceInfo
{
    /// <summary>The package identifier, verbatim.</summary>
    string Name { get; }

    /// <summary>
    ///     The file that declares the reference, as an absolute path. It may be a props file above the project
    ///     rather than the project file itself. Where the project declares the package more than once, this is the
    ///     declaration that decided <see cref="IsPrivate" />.
    /// </summary>
    string FilePath { get; }

    /// <summary>The 1-based line of that declaration in <see cref="FilePath" />.</summary>
    int Line { get; }

    /// <summary>
    ///     Whether the reference is private: its <c>PrivateAssets</c> metadata covers every asset, usually written
    ///     <c>PrivateAssets="all"</c>, so the package is neither listed as a dependency of the package this project
    ///     produces nor passed on to a project that references this one. Analyzers, source generators and
    ///     build-time tools are declared this way, and <c>MustReferenceNoPackages</c> does not count these
    ///     references. The value is the one the build evaluates, so a default from an <c>ItemDefinitionGroup</c>
    ///     or an <c>Update</c> in a props file counts, while <see cref="FilePath" /> stays at the <c>Include</c>,
    ///     and a <c>GlobalPackageReference</c> under central package management is private, declared at the first
    ///     line of the project file. <c>IncludeAssets</c> and <c>ExcludeAssets</c> do not decide it:
    ///     <c>ExcludeAssets="all"</c> alone still passes the package on to a referencing project, and
    ///     <c>IncludeAssets="analyzers"</c> alone still lists it as a dependency. Where a project declares one
    ///     package more than once, including once per target framework, the package is private only if every
    ///     declaration is.
    /// </summary>
    bool IsPrivate { get; }
}
