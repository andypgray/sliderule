using SlideRule.Extraction.Fragments;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Roslyn.Caching;

/// <summary>
///     The persisted extraction cache as one serializable document, which <see cref="ExtractionCacheStore" />
///     writes to one atomic file: a self-describing manifest plus every <see cref="CodebaseFragment" /> the last
///     workspace-loaded run extracted, in cold (ordinal-project) order. The manifest records every input path and
///     its stamp, so validation needs no MSBuild and no fragment inspection.
/// </summary>
/// <remarks>
///     The JSON shape is the cache's on-disk schema, versioned by <paramref name="SchemaVersion" />;
///     <paramref name="ToolVersion" /> is <see cref="FileStamping.CurrentToolVersion" /> at write time, and
///     <paramref name="StructuralStamps" /> stamps every path
///     <see cref="ProjectCone.SolutionStructuralPaths(string, IEnumerable{ProjectInputs})" /> yields, absent files
///     included. <paramref name="Diagnostics" /> and the project lists after it are the
///     recorded run's load verdict, field for field the <see cref="WorkspaceDiagnostics" /> its load reported,
///     stored because a hit opens no workspace to recompute them from; each unsupported project's kind is stored
///     rather than re-derived, so a cold run and a hit stay word-identical.
/// </remarks>
internal sealed record CacheManifest(
    int SchemaVersion,
    string ToolVersion,
    IReadOnlyList<FileStamp> StructuralStamps,
    IReadOnlyList<ProjectCacheEntry> Projects,
    IReadOnlyList<SpecResolutionRecord> SpecResolutions,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> FailedProjects,
    IReadOnlyList<string> UncheckedProjects,
    IReadOnlyList<string> RestoreFailedProjects,
    IReadOnlyList<UnsupportedProject> UnsupportedProjects,
    IReadOnlyList<CodebaseFragment> Fragments);

/// <summary>
///     A file's recorded existence and content fingerprint at cache-write time, the durable cross-process
///     counterpart of a <see cref="FileFreshness" /> capture, for a structural input or a source document.
/// </summary>
/// <remarks>
///     <paramref name="Path" /> is absolute and OS-native, stored verbatim, and <paramref name="Sha256" /> is
///     lowercase hex, null for a file that could not be read. An absent file is still recorded, with
///     <paramref name="Exists" /> false, zero ticks and length and no hash, so a file that appears later is an
///     existence flip validation catches. <paramref name="Promoted" /> is the frozen racy-window verdict,
///     <see cref="FileFreshness.IsPromoted" /> at capture: whether a stat match alone proves the file unchanged.
///     <paramref name="Shape" /> is the document's <see cref="SourceShape" />, null for a structural stamp, the
///     binlog copy, an absent file or a document that could not be read.
/// </remarks>
internal sealed record FileStamp(
    string Path,
    bool Exists,
    long LastWriteTimeUtcTicks,
    long Length,
    string? Sha256,
    bool Promoted,
    SourceShape? Shape = null);

/// <summary>
///     One cached project, keyed by its project (assembly) name, which ties the entry to its fragments. A
///     multi-targeted project has one entry, its several <see cref="Microsoft.CodeAnalysis.Project" />s sharing
///     the name the load boundary normalized them to, but one <see cref="CodebaseFragment" /> per framework.
/// </summary>
/// <remarks>
///     <paramref name="CsprojPath" /> is absolute and also in the structural set.
///     <paramref name="ProjectReferences" /> names the projects this one references, the Merkle dependency
///     edges: the compiler's closure, which invalidation has to follow, not the declared list the fragments
///     carry. <paramref name="ContentKey" /> hashes the project's own inputs, <paramref name="SurfaceKey" /> the
///     same inputs with each document's <see cref="SourceShape.SurfaceHash" /> in place of its bytes, and
///     <paramref name="MerkleKey" /> the content key with the surface keys of the project's dependency cone.
/// </remarks>
// ContentKey and SurfaceKey are persisted schema: validation recomputes keys bottom-up from documents rather
// than reading the stored values, which exist so a manifest diff shows whether a project's own content, its
// declaration surface, or only its dependency cone moved.
internal sealed record ProjectCacheEntry(
    string ProjectName,
    string CsprojPath,
    string ProjectDirectory,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<FileStamp> Documents,
    string ContentKey,
    string MerkleKey,
    string SurfaceKey);

/// <summary>
///     A recorded spec resolution, keyed by the normalized <c>--spec</c> argument that produced it, so a cache hit
///     replays spec resolution without a workspace and resolves as a cold run did, down to what the built-output
///     search refuses and its error text.
/// </summary>
/// <param name="NormalizedSpecArgument">
///     The normalized <c>--spec</c> value this record resolves (an absolute csproj/dll path, or the empty
///     string for the no-<c>--spec</c> convention default).
/// </param>
/// <param name="SpecProjectName">The solution-member spec project's name, or null for an explicit DLL.</param>
/// <param name="ExcludeProjectNames">
///     Every project to drop from the checked universe: the spec project plus the private plumbing only it
///     references (<c>SpecExclusion</c>).
/// </param>
/// <param name="OutputFilePaths">
///     Every Debug-evaluated output path of the spec project, one per framework, ordinal-sorted, or empty for an
///     explicit DLL, so a hit loads the framework's DLL the cold run chose.
/// </param>
/// <param name="IntermediateAssemblyPath">
///     The spec project's intermediate (<c>obj</c>-side) assembly path, or null when the workspace did not
///     carry one. One path serves every framework: the built-output search derives the intermediate root from the
///     prefix it shares with the evaluated path, and that root is the same whichever framework's pair it starts from.
/// </param>
internal sealed record SpecResolutionRecord(
    string NormalizedSpecArgument,
    string? SpecProjectName,
    IReadOnlyList<string> ExcludeProjectNames,
    IReadOnlyList<string> OutputFilePaths,
    string? IntermediateAssemblyPath);
