using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;

namespace SlideRule.Cli.Verbs;

/// <summary>The parsed inputs to a <c>check</c> run — free of Roslyn types so it crosses the MSBuild gate.</summary>
/// <remarks>
///     <paramref name="Solution" /> is a solution file or a directory, and <paramref name="Spec" /> a built DLL or a
///     solution-member csproj, both with the settings file already applied; a null <paramref name="Solution" />,
///     <paramref name="Spec" /> or <paramref name="Binlog" /> leaves the choice to discovery, the spec convention and
///     <see cref="MsBuildGate" />. <paramref name="HookJson" /> renders for a Claude Code hook and is never set beside
///     <paramref name="Json" />: two documents, one stdout. <paramref name="HookEvent" /> is the event that document
///     names, one of <see cref="Rendering.HookReportRenderer.Events" /> or null for the default; Claude Code rejects a
///     hook document that names any event but the one that fired. <paramref name="AllowWorkspaceDiagnostics" />
///     checks the partial model instead of failing closed when a project fails to load or restore.
///     <paramref name="Grain" /> is the floor on how much of each rule the JSON document renders; a transport with a
///     response budget may go coarser (<see cref="IResponseFitter" />). <paramref name="ChangedFiles" />, absolute
///     paths, replaces the <paramref name="DiffBase" /> listing.
/// </remarks>
internal sealed record CheckRequest(
    string? Solution,
    string? Spec,
    bool Json,
    bool HookJson,
    string? DiffBase,
    string WorkingDirectory,
    bool NoCache,
    string? Binlog,
    bool AllowWorkspaceDiagnostics,
    string? Sarif,
    string? Rules,
    DocumentGrain Grain,
    string? HookEvent = null,
    IReadOnlyList<string>? ChangedFiles = null) : IReplayableRequest;
