namespace SlideRule;

/// <summary>
///     What a project predicate sees: the facts of one project as a build artifact, handed to the
///     lambda in <c>.Where(project =&gt; ..., description)</c> and
///     <c>.Must(project =&gt; ..., description)</c> on a project selection. The facts are what a build
///     evaluation answers, not what the project file spells, so a setting supplied by a shared props
///     file above the project, or defaulted by the SDK, is a fact here and absent from the file. The
///     evaluation runs on the machine running the check, so a condition on the operating system or an
///     environment variable resolves there, and a fact behind one can differ between Windows and Linux. Where
///     nothing evaluated a project the fact is missing rather than defaulted (the frameworks list only
///     what compiled separately, the other lists come back empty and the tri-state facts null), so a
///     predicate that reads unknown as false asserts something nothing measured; treat unknown as
///     passing, as the project verbs do. The lambda runs when the check runs, not when the spec is
///     loaded; the description beside it is required, and it is the description, never the lambda, that
///     the generated agent context and the check report print. New facts are added in later versions and the ones here
///     keep their meaning.
/// </summary>
// Read by the predicate authors this contract exists for rather than through the interface
// in-solution, so solution-wide search finds only the implementations and calls the members unused.
// ReSharper disable UnusedMemberInSuper.Global
public interface IProjectInfo
{
    /// <summary>The project's name, as the solution lists it and as <c>Named</c> matches it.</summary>
    string Name { get; }

    /// <summary>
    ///     Every framework the project targets, as short monikers (<c>net10.0</c>, <c>netstandard2.0</c>,
    ///     and <c>net48</c> for a project that spells its framework the old way), ordered ordinally. Where
    ///     nothing evaluated the project, it lists only the frameworks the project was compiled for one at a
    ///     time, and is empty for a project compiled once.
    /// </summary>
    IReadOnlyList<string> TargetFrameworks { get; }

    /// <summary>
    ///     The packages the project declares itself, ordered by name, each carrying the file and line of
    ///     its declaration (which may be a props file above the project), whether it is private
    ///     (<see cref="IPackageReferenceInfo.IsPrivate" />) and the frameworks that declare it
    ///     (<see cref="IPackageReferenceInfo.Frameworks" />). Every declared reference is here, private ones
    ///     included, so <c>p.PackageReferences.Count == 0</c> is stricter than
    ///     <c>MustReferenceNoPackages</c>, which does not count private references. Declared references
    ///     only: the packages those packages pull in are a different fact and are not here.
    /// </summary>
    IReadOnlyList<IPackageReferenceInfo> PackageReferences { get; }

    /// <summary>
    ///     The names of the projects this project declares a reference to, ordered ordinally. Declared
    ///     references only: a project reached only through another project's references is not listed, and
    ///     neither is a reference that only orders the build (<c>ReferenceOutputAssembly="false"</c>).
    /// </summary>
    IReadOnlyList<string> ProjectReferences { get; }

    /// <summary>
    ///     Whether the project produces a NuGet package, or null where nothing evaluated it.
    /// </summary>
    bool? IsPackable { get; }

    /// <summary>
    ///     Whether restoring the project writes a package lock file, or null where nothing evaluated it.
    /// </summary>
    bool? LocksPackages { get; }

    /// <summary>
    ///     The MSBuild properties a declaration in the solution sets for the project, ordered by name, one entry
    ///     for each property in each target framework's build: its evaluated value, the framework
    ///     (<see cref="IProjectPropertyInfo.TargetFramework" />), and the file and line of the definition that
    ///     won, which may be a props file above the project. A value only the SDK or a package supplies is not
    ///     here, and neither is one passed on the command line or read from the environment, so
    ///     <c>p.Properties.Any(x =&gt; x.Name == "IsPackable")</c> asks whether anybody in the solution set it.
    ///     Empty where nothing evaluated the project.
    /// </summary>
    IReadOnlyList<IProjectPropertyInfo> Properties { get; }
}
