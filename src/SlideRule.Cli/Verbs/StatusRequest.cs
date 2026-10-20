using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Verbs;

/// <summary>The parsed inputs for <c>sliderule status</c> — the burndown report (human or <c>--json</c>).</summary>
/// <remarks><paramref name="AllowWorkspaceDiagnostics" /> opts out of <see cref="IncompleteModelGate" />.</remarks>
internal sealed record StatusRequest(
    string? Solution,
    string? Spec,
    bool Json,
    string WorkingDirectory,
    bool NoCache,
    string? Binlog,
    bool AllowWorkspaceDiagnostics) : IReplayableRequest;
