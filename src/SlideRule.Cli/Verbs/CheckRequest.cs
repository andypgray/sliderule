using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;

namespace SlideRule.Cli.Verbs;

/// <summary>The parsed inputs to a <c>check</c> run — free of Roslyn types so it crosses the MSBuild gate.</summary>
/// <param name="Solution">
///     The solution to load: the settings file's where one applies, otherwise the positional argument (a file, a
///     directory, or null for discovery).
/// </param>
/// <param name="Spec">
///     The <c>--spec</c> value (a built DLL or a solution-member csproj), else the settings file's spec, else null
///     for the convention.
/// </param>
/// <param name="Json">Whether to emit the machine-readable JSON document instead of human output.</param>
/// <param name="HookJson">
///     Whether to render for a Claude Code hook. A clean run carrying at least one warning writes the hook
///     document (<see cref="Rendering.HookReportRenderer" />) and nothing else, so the hook passes stdout
///     straight through and the warning reaches the agent as transcript context; a clean run with no
///     warnings writes nothing at all; every other outcome writes what it always wrote, which is what the
///     hook blocks with. The <c>hook</c> verb sets it on the check it runs, and nothing else does. Nothing
///     may share that stdout, so the run's whole human side — stamps, report and diagnostics alike — is
///     composed into one buffer and then placed, rather than streamed across two channels. Never set beside
///     <see cref="Json" />: two documents, one stdout.
/// </param>
/// <param name="DiffBase">
///     The <c>--diff-base</c> git ref the scope tripwires list changed files from, or null, which skips them unless
///     <see cref="ChangedFiles" /> lists the files instead.
/// </param>
/// <param name="WorkingDirectory">The directory solution discovery walks up from.</param>
/// <param name="NoCache">Whether to bypass the persisted extraction cache entirely (no read, no write).</param>
/// <param name="Binlog">
///     The explicit <c>--binlog</c> path to replay instead of a design-time build, or null to auto-select
///     (a valid build capture if one exists, else a design-time build). Roslyn-free — the gate acts on it.
/// </param>
/// <param name="AllowWorkspaceDiagnostics">
///     Whether to check against the partial model when a project fails to load or its NuGet packages do not
///     resolve. Default (<c>false</c>): either fails the run with exit 2, overriding the 0/1 verdict, because
///     a rule that "passes" only because a project did not load is worse than no answer. <c>true</c> lets the
///     run exit 0/1 against the partial model instead. Keys strictly on those projects and never on diagnostic
///     text: the load's messages, the merge notes and the NuGet audit advisories all render whichever way
///     this is set.
/// </param>
/// <param name="Sarif">
///     The <c>--sarif</c> path to write a SARIF 2.1.0 report to — a third renderer over the same result
///     model (for GitHub/ADO code scanning et al.) — or null to write none. Human and <c>--json</c> output
///     are byte-for-byte unchanged either way; the only added observable is a <c>wrote &lt;path&gt;</c>
///     line in human mode. Roslyn-free, so the record still crosses the MSBuild gate.
/// </param>
/// <param name="Rules">
///     The <c>--rules</c> allow-list — rule-ID globs, semicolon-separated — or null for every rule in the
///     spec. It narrows what <em>runs</em>, not what is displayed, so the summary and the 0/1 verdict cover
///     the subset alone. A filter that matches no rule refuses the run rather than checking nothing.
/// </param>
/// <param name="Grain">
///     The floor on how much of each rule the JSON document renders: never finer than this, and a caller
///     whose transport has a response budget may take it coarser still (<see cref="IResponseFitter" />).
///     The opposite knob to <see cref="Rules" /> and independent of it — every selected rule is reported at
///     every rung, in less detail. Human output ignores it: a terminal has no budget to overrun.
/// </param>
/// <param name="HookEvent">
///     Which Claude Code event the <see cref="HookJson" /> document names, one of
///     <see cref="Rendering.HookReportRenderer.Events" />, or null for
///     <see cref="Rendering.HookReportRenderer.DefaultEvent" />. The field decides whether the report reaches the
///     agent at all: Claude Code reads <c>additionalContext</c> only from a document naming the event it fired,
///     so a turn-end hook handed the per-edit envelope is silently ignored. The <c>hook</c> verb sets it from
///     the event that fired.
/// </param>
/// <param name="ChangedFiles">
///     The files the scope tripwires are to treat as changed, as absolute paths, or null for the tripwires to read
///     <see cref="DiffBase" /> instead. The <c>hook</c> verb sets it to the files a turn changed, which it has
///     already listed, so the run lists none of its own.
/// </param>
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
