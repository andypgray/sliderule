using SlideRule.Roslyn.Caching;

namespace SlideRule.Roslyn.Replay;

/// <summary>
///     The persisted build capture as a single serializable document: a self-describing manifest recording
///     exactly what makes the copied binlog (<c>capture.binlog</c>) a faithful stand-in for a design-time
///     build of the current tree, written and validated by <see cref="BinlogCaptureStore" />.
/// </summary>
/// <remarks>
///     <paramref name="StructuralStamps" /> are full <see cref="FileStamp" />s, SHA-256 and absence included, of
///     the same structural set <c>ExtractionCacheStore</c> fingerprints. <paramref name="ToolVersion" /> is the
///     writing tool's informational version, per-commit during development. <paramref name="BinlogCopyStamp" />
///     is recorded for completeness: the copy's bytes are never re-hashed against it.
/// </remarks>
internal sealed record CaptureManifest(
    int SchemaVersion,
    string ToolVersion,
    IReadOnlyList<FileStamp> StructuralStamps,
    IReadOnlyList<CaptureProjectEntry> Projects,
    FileStamp BinlogCopyStamp);

/// <summary>
///     One replayed project's identity and its full csc source-file list, as the capture records it, with
///     absolute paths.
/// </summary>
/// <remarks>
///     <para>
///         <paramref name="DocumentPaths" /> is the replayed solution's whole document list, obj-generated sources
///         included, existence-checked and never hashed at validation.
///     </para>
///     <para>
///         <paramref name="ConeFiles" /> is the project cone's <c>*.cs</c> (bin/obj excluded) as it stood at
///         ingest. The cone is a superset of <paramref name="DocumentPaths" />: a <c>&lt;Compile Remove&gt;</c>'d
///         or <c>None</c>-typed <c>*.cs</c> is in the cone but not compiled, so without this snapshot the cone scan
///         would read it as a perpetual add and invalidate the capture on every run.
///     </para>
/// </remarks>
// ProjectName and the two evaluated paths are persisted schema that validation never reads back: validation
// keys on the directory, csproj and document set, and the manifest records these so a diff of it shows each
// project's identity and what decided which structural paths the capture stamped.
internal sealed record CaptureProjectEntry(
    string ProjectName,
    string CsprojPath,
    string ProjectDirectory,
    IReadOnlyList<string> DocumentPaths,
    IReadOnlyList<string> ConeFiles,
    string? EvaluatedOutputPath = null,
    string? IntermediateAssemblyPath = null);
