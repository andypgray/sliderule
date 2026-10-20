using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The parsed inputs for <c>sliderule graph</c> — the pre-spec codebase survey (human or <c>--json</c>).
/// </summary>
/// <remarks>
///     <paramref name="AllowWorkspaceDiagnostics" /> opts out of <see cref="IncompleteModelGate" />.
///     <paramref name="Grain" /> is the floor on how much detail the survey renders; a transport with a response
///     budget may go coarser (<see cref="IResponseFitter" />). <paramref name="Projects" /> is the
///     <c>--projects</c> list of project-name globs, or null for every project.
/// </remarks>
internal sealed record GraphRequest(
    string? Solution,
    bool Json,
    string WorkingDirectory,
    bool NoCache,
    string? Binlog,
    bool AllowWorkspaceDiagnostics,
    DocumentGrain Grain,
    string? Projects) : IReplayableRequest;
