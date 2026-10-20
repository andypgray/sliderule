using SlideRule.Extraction.Fragments;

namespace SlideRule.Extraction;

/// <summary>
///     What one evaluation of one project file said about the artifact it builds, one value per
///     <em>compilation</em> rather than per project file: a condition on <c>$(TargetFramework)</c> can add a
///     package to one framework and not another, and an outer-build evaluation would see neither.
/// </summary>
/// <remarks>
///     Each of <paramref name="TargetFrameworksSite" />, <paramref name="IsPackableSite" /> and
///     <paramref name="LocksPackagesSite" /> is the <c>file:line</c> of the declaration that decided its fact, or
///     the project file where no declaration the repository owns decided it. <paramref name="TargetFrameworks" />
///     is ordinal-ordered and normalized to the short moniker, and empty where the project declares none this
///     reader recognises. <paramref name="PackageReferences" /> holds the packages the project declares, ordinal
///     by name, never the SDK's implicit references or the transitive closure. <paramref name="IsPackable" /> is
///     <see langword="null" /> where the property is undefined: a project that imports no pack targets has no
///     answer rather than a negative one. An undeclared lock policy reads as <paramref name="LocksPackages" />
///     <see langword="false" />. <paramref name="ProjectReferencePaths" /> holds the full path of each project
///     file the project declares a reference to, in declaration order, never the SDK's transitive closure or an
///     item that only orders the build (<c>ReferenceOutputAssembly="false"</c>).
/// </remarks>
internal sealed record ProjectArtifactFacts(
    IReadOnlyList<string> TargetFrameworks,
    FragmentSite TargetFrameworksSite,
    IReadOnlyList<FragmentPackageReference> PackageReferences,
    bool? IsPackable,
    FragmentSite IsPackableSite,
    bool LocksPackages,
    FragmentSite LocksPackagesSite,
    IReadOnlyList<string> ProjectReferencePaths,
    FragmentProperties Properties);

/// <summary>
///     What one enumeration of a solution produced: the compilations to extract from, in extraction order, and
///     at the same index what MSBuild said about each one's project, <see langword="null" /> where that project
///     could not be evaluated.
/// </summary>
internal sealed record ExtractionInputs(
    IReadOnlyList<CompilationInput> Inputs,
    IReadOnlyList<ProjectArtifactFacts?> ArtifactFacts);
