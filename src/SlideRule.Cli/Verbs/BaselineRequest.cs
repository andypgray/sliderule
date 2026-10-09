using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The parsed inputs for a <c>baseline</c> subcommand. The <see cref="Mode" /> is the subcommand, so exactly
///     one mode is a fact of the command tree rather than a check. The <c>add</c> companions ride in
///     <see cref="Addition" />, which only that subcommand builds, and are validated together before any workspace
///     cost; the <c>rename</c> companions ride in <see cref="Rename" /> the same way.
/// </summary>
/// <param name="Solution">
///     The solution to load: the settings file's where one applies, otherwise the positional argument (a file, a
///     directory, or null for discovery).
/// </param>
/// <param name="Spec">
///     The <c>--spec</c> value (a built DLL or a solution-member csproj), else the settings file's spec, else null
///     for the convention.
/// </param>
/// <param name="Mode">The subcommand the run was invoked as.</param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from.</param>
/// <param name="NoCache">
///     Whether to bypass the persisted extraction cache entirely (no read, no write). It is the operator's
///     half of the policy only for <see cref="BaselineMode.Add" />, which records one violation the run did see;
///     <c>init</c> and <c>accept-reductions</c> read absence as evidence and force a cache-free extraction
///     whatever this says, and <see cref="BaselineMode.Rename" /> never extracts at all.
/// </param>
/// <param name="AllowWorkspaceDiagnostics">
///     Whether to write baselines from the partial model when a project fails to load. Default
///     (<c>false</c>): any workspace-load failure refuses the whole command with exit 2 and writes nothing,
///     because a baseline is the team's signature on its debt and a partial model has no idea what that debt
///     is. Keys on exactly what <c>check</c> keys on (<see cref="IncompleteModelGate" />). <c>rename</c> measures
///     nothing and never gates, so it always passes <c>false</c>.
/// </param>
/// <param name="Addition">The <c>add</c> companions, or null for the other three subcommands.</param>
/// <param name="Rename">The <c>rename</c> companions, or null for the other three subcommands.</param>
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
