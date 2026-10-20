using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

// The wire shape of `graph --json` — the pre-spec codebase survey, its own document with its own
// schemaVersion (GraphJsonRenderer.SchemaVersion), distinct from check and status, and a projection of
// GraphSummary, whose members say what each row means. Serialized camelCase, indented, nulls omitted. The
// optional slots below — grain, projectsScope, the coverage statements, and the workspace ones — are additive
// and null (omitted) on a full, unscoped survey of an all-C# solution whose load raised nothing, whose NuGet
// packages resolved and that no solution filter narrowed, so the schema version does not move and the
// default document is byte-identical to the one before they existed. A run whose model is incomplete reaches
// this document only under --allow-workspace-diagnostics, since graph otherwise refuses before extraction.
//
// Grain thins the survey as DocumentGrain's rungs say, and a slot a rung elides is null: "not rendered at
// this grain", never "none found". Each project row thins as GraphProjectJson says, from overview down;
// skeleton drops the external-reference rows and the two coverage statements about the survey's contents,
// multiplyDeclaredTypes and shadowedTypes; index drops the observed project edges and the load's packageAudit
// and workspaceDiagnostics. Each dropped array gives way to its count: projectEdges and externalEdges are
// written even when empty, so their counts appear whenever they are elided, while the rest are omitted when
// empty, so theirs appear only where rows were dropped, never as a bare 0. The trust stamps describe the load,
// so no grain elides them and projectsScope does not narrow them.

/// <summary>The root <c>graph --json</c> document.</summary>
/// <remarks>
///     <para>
///         <paramref name="Solution" /> is the solution's file name, never a path, and <paramref name="Grain" /> is
///         <see cref="DocumentGrains.Wire" />. <paramref name="ProjectsScope" /> is the project-name globs the
///         survey was narrowed to; present, it explains why <c>projectEdges</c> can name a project <c>projects</c>
///         does not: an edge survives scoping on either endpoint.
///     </para>
///     <para>
///         The trust stamps, <paramref name="ModelIncomplete" /> through <paramref name="UnsupportedProjects" />,
///         are <see cref="WorkspaceTrustStamp" />'s. An incomplete model leaves whole projects, with their types
///         and edges, missing from the survey. A restore-failed project is here with all its types, but
///         <c>externalEdges</c> lacks the rows its package references would have produced, so a short list can
///         read as a codebase with few dependencies. A project or edge absent from a survey that carries unchecked
///         or unsupported projects may be out of view.
///     </para>
/// </remarks>
internal sealed record GraphJson(
    int SchemaVersion,
    string Solution,
    string? Grain,
    IReadOnlyList<string>? ProjectsScope,
    IReadOnlyList<GraphProjectJson> Projects,
    IReadOnlyList<GraphProjectEdgeJson>? ProjectEdges,
    int? ProjectEdgeCount,
    IReadOnlyList<GraphExternalEdgeJson>? ExternalEdges,
    int? ExternalEdgeCount,
    IReadOnlyList<GraphMultiplyDeclaredTypeJson>? MultiplyDeclaredTypes,
    int? MultiplyDeclaredTypeCount,
    IReadOnlyList<GraphShadowedTypeJson>? ShadowedTypes,
    int? ShadowedTypeCount,
    IReadOnlyList<PackageAuditEntry>? PackageAudit,
    int? PackageAuditCount,
    IReadOnlyList<string>? WorkspaceDiagnostics,
    int? WorkspaceDiagnosticCount,
    bool? ModelIncomplete,
    IReadOnlyList<string>? FailedProjects,
    IReadOnlyList<string>? UncheckedProjects,
    IReadOnlyList<string>? RestoreFailedProjects,
    IReadOnlyList<UnsupportedProjectStamp>? UnsupportedProjects);

/// <summary>
///     One project row of the survey, a <see cref="SlideRule.Codebase.ProjectSummary" /> with its packages as name
///     lists. Its namespace inventory goes at overview grain, its declared packages at skeleton, and at index
///     grain the row is down to what names and sizes the project: <c>name</c>, <c>solutionMember</c>,
///     <c>types</c> and <c>generated</c>.
/// </summary>
/// <remarks>
///     <para>
///         <paramref name="ProjectReferences" />, <paramref name="PackageReferences" /> and
///         <paramref name="Namespaces" /> are written even when empty; every other slot with nothing to say is
///         absent, so a missing key is never a <c>false</c> or an empty list. <paramref name="SolutionMember" />
///         absent means membership was never read. <paramref name="IsPackable" /> and
///         <paramref name="LocksPackages" /> are evaluated rather than read off the project file: the first is
///         true by SDK default on a project that never says so, and the second is regularly set once in a props
///         file above the solution. Both are absent where nothing evaluated the project, and
///         <paramref name="IsPackable" /> also for a project outside the SDK's pack machinery.
///     </para>
///     <para>
///         <paramref name="PrivatePackageReferences" /> is the subset of <paramref name="PackageReferences" />
///         whose <c>PrivateAssets</c> is all, in the same order. <paramref name="PackageFrameworks" /> names the
///         packages declared for only some of the project's frameworks, a <c>PackageReference</c> conditioned on
///         <c>$(TargetFramework)</c>, and is absent too where the frameworks were not told apart. A non-empty
///         <paramref name="UndeclaredProjectReferences" /> says <paramref name="ProjectReferences" /> is short,
///         directly or through what a declared project references in turn: the project file declares a reference
///         this survey could not read, most often through a property only a solution build defines.
///     </para>
/// </remarks>
internal sealed record GraphProjectJson(
    string Name,
    bool? SolutionMember,
    IReadOnlyList<string>? TargetFrameworks,
    string? FactsFollow,
    bool? IsPackable,
    bool? LocksPackages,
    IReadOnlyList<string>? ProjectReferences,
    IReadOnlyList<string>? UndeclaredProjectReferences,
    IReadOnlyList<string>? PackageReferences,
    IReadOnlyList<string>? PrivatePackageReferences,
    IReadOnlyList<GraphPackageFrameworksJson>? PackageFrameworks,
    int Types,
    int? Generated,
    IReadOnlyList<GraphNamespaceJson>? Namespaces);

/// <summary>
///     A package the project declares for only some of its target frameworks, and those frameworks, ordered
///     ordinally.
/// </summary>
internal sealed record GraphPackageFrameworksJson(string Package, IReadOnlyList<string> Frameworks);

/// <summary>
///     A namespace and the count of the project's declared types in it, with how many of those a generator
///     emitted.
/// </summary>
internal sealed record GraphNamespaceJson(string Namespace, int Types, int? Generated);

/// <summary>An observed cross-project reference edge with its distinct type-pair count.</summary>
internal sealed record GraphProjectEdgeJson(string Source, string Target, int References);

/// <summary>An external reference grouped by target namespace root, with its distinct type-pair count.</summary>
internal sealed record GraphExternalEdgeJson(string Source, string TargetNamespaceRoot, int References);

/// <summary>
///     One type several projects declare: its full name, every declaring project, and the declarer whose
///     facts won.
/// </summary>
internal sealed record GraphMultiplyDeclaredTypeJson(
    string Type,
    IReadOnlyList<string> DeclaredBy,
    string FactsFollow);

/// <summary>
///     One full name that denotes two types: the project declaring it, the referenced assemblies supplying
///     it, and the projects whose references reach the assembly's rather than the declaration.
/// </summary>
internal sealed record GraphShadowedTypeJson(
    string Type,
    string DeclaredBy,
    IReadOnlyList<string> SuppliedBy,
    IReadOnlyList<string> BoundFromAssemblyBy);
