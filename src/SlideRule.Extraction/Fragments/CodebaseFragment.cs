namespace SlideRule.Extraction.Fragments;

/// <summary>
///     The extraction of one <see cref="CompilationInput" /> (one project, one target framework) as pure,
///     self-contained data, which the persisted extraction cache stores as it stands.
/// </summary>
/// <remarks>
///     <para>
///         No record of the fragment holds a Roslyn type (no <c>ISymbol</c>, <c>Compilation</c> or
///         <c>Location</c>) and its enums are the Core ones, so it round-trips through System.Text.Json
///         unchanged. Every site list is distinct and in <see cref="FragmentSite" /> order, and no edge list
///         holds a self-edge. A fragment is one compilation's narrow view: a type another project declares
///         still appears in its <see cref="Externals" />, and the merge decides unification. Its collections
///         are in a canonical order so serialization is stable, and the merge re-derives global order, so a
///         fragment's internal order never affects the model.
///     </para>
///     <para>
///         <see cref="TargetFramework" /> is the other half of the identity <see cref="ProjectName" /> starts:
///         where one project file yielded several compilations, the project name alone does not say which one
///         a fact came from. It is null for a single-framework project. The parameters from
///         <see cref="SolutionMember" /> on carry defaults so that a hand-built fragment need not name facts it
///         has no way to know, and null in them reads as unknown, never as off, except in
///         <see cref="UndeclaredProjectReferences" />, where null and empty both mean nothing to report.
///         <see cref="AssemblyName" /> is what the compilation emits, which an <c>&lt;AssemblyName&gt;</c>
///         property or a renamed csproj parts from <see cref="ProjectName" />, the csproj's own file name; it is
///         the string comparable against <see cref="FragmentExternal.AssemblyName" />. The artifact facts come
///         from MSBuild rather than the compilation, each with the <c>file:line</c> that decided it, regularly a
///         props file above the project. <see cref="DeclaredTargetFrameworks" /> is every framework the project
///         file names, the same in each fragment of a multi-targeted project.
///     </para>
/// </remarks>
internal sealed record CodebaseFragment(
    string ProjectName,
    string? TargetFramework,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<FragmentType> DeclaredTypes,
    IReadOnlyList<FragmentExternal> Externals,
    IReadOnlyList<FragmentEdge> Edges,
    IReadOnlyList<FragmentMemberEdge> MemberEdges,
    IReadOnlyList<FragmentConstructorEdge> ConstructorEdges,
    IReadOnlyList<FragmentInjectionEdge> InjectionEdges,
    IReadOnlyList<FragmentCatchEdge> CatchEdges,
    IReadOnlyList<FragmentThrowEdge> ThrowEdges,
    IReadOnlyList<FragmentExposureEdge> ExposureEdges,
    IReadOnlyList<FragmentServiceRegistration> ServiceRegistrations,
    bool? SolutionMember = null,
    string? AssemblyName = null,
    IReadOnlyList<string>? DeclaredTargetFrameworks = null,
    FragmentSite? TargetFrameworksSite = null,
    IReadOnlyList<FragmentPackageReference>? PackageReferences = null,
    bool? IsPackable = null,
    FragmentSite? IsPackableSite = null,
    bool? LocksPackages = null,
    FragmentSite? LocksPackagesSite = null,
    IReadOnlyList<string>? UndeclaredProjectReferences = null,
    FragmentProperties? Properties = null);
