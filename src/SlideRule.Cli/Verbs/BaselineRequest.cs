using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The parsed inputs for a <c>baseline</c> subcommand. <paramref name="Mode" /> is the subcommand;
///     <paramref name="Addition" /> carries the <c>add</c> companions and <paramref name="Rename" /> the
///     <c>rename</c> ones, each null under every other mode.
/// </summary>
/// <remarks><paramref name="AllowWorkspaceDiagnostics" /> opts out of <see cref="IncompleteModelGate" />.</remarks>
internal sealed record BaselineRequest(
    string? Solution,
    string? Spec,
    BaselineMode Mode,
    string WorkingDirectory,
    bool NoCache,
    bool AllowWorkspaceDiagnostics,
    BaselineAddition? Addition = null,
    BaselineRename? Rename = null)
{
    /// <summary>
    ///     Whether this mode reads a violation's absence as evidence: <c>init</c> records "zero debt" for what the
    ///     run did not see, and <c>accept-reductions</c> deletes entries whose violation stopped. Both extract
    ///     cache-free and refuse a narrowed universe; <c>add</c> records only what the run did see.
    /// </summary>
    internal bool ReadsAbsenceAsEvidence => Mode is BaselineMode.Init or BaselineMode.AcceptReductions;
}
