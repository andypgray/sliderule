using SlideRule.Cli.Rendering;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn;

namespace SlideRule.Cli.Mcp;

/// <summary>
///     The solution + spec this MCP server is bound to for its lifetime, captured once at
///     <c>sliderule mcp</c> startup, and the run policy every tool call applies on top of them.
///     Resolution (solution discovery, spec load, workspace open) happens per tool call, not here, so any
///     resolution error text matches the CLI exactly.
/// </summary>
/// <remarks>
///     <paramref name="Solution" /> and <paramref name="Spec" /> take the forms <see cref="Verbs.CheckRequest" />
///     describes; <paramref name="WorkingDirectory" /> is the server's launch directory, which discovery walks up
///     from. How a tool call differs from a CLI run is the run policy the constants below name, one leg each, so no
///     tool body can spell one wrong and no leg can change in one place only.
/// </remarks>
internal sealed record McpServerBinding(string? Solution, string? Spec, string WorkingDirectory)
{
    /// <summary>The tools answer with documents, never the human rendering: always <c>--json</c>.</summary>
    private const bool AsJsonDocument = true;

    /// <summary>
    ///     Never the hook document: that one shapes a command hook's stdout around an exit code, and a tool
    ///     call has neither. A client that wants the warnings reads them from the document's own
    ///     <c>warnings</c> arrays.
    /// </summary>
    private const bool NoHookDocument = false;

    /// <summary>
    ///     A tool call never reads or writes <c>cache.json</c>: the warm session and the persisted cache keep
    ///     independent lifetimes, so they can never race on the file.
    /// </summary>
    private const bool BypassPersistedCache = true;

    /// <summary>The warm path never uses the build capture — latency-critical callers ride the session.</summary>
    private const string? NoBinlogReplay = null;

    /// <summary>
    ///     The incompleteness rides the document (<c>workspaceDiagnostics</c> + <c>modelIncomplete</c>)
    ///     rather than an exit code this surface does not have, so the run answers instead of refusing.
    /// </summary>
    private const bool ReportPartialModels = true;

    /// <summary>No SARIF file: the tools return their document, and write nothing to disk.</summary>
    private const string? NoSarifFile = null;

    /// <summary>
    ///     The <c>arch_check</c> run. The rule globs go in raw, so the same parse and the same
    ///     unmatched-filter refusal serve both surfaces.
    /// </summary>
    internal CheckRequest CheckRequest(string? diffBase, string? rules, DocumentGrain grain)
    {
        return new CheckRequest(
            Solution, Spec, AsJsonDocument, NoHookDocument, Given(diffBase), WorkingDirectory, BypassPersistedCache,
            NoBinlogReplay, ReportPartialModels, NoSarifFile, rules, grain);
    }

    /// <summary>The <c>arch_status</c> run: the whole burndown, which carries no narrowing knob.</summary>
    internal StatusRequest StatusRequest()
    {
        return new StatusRequest(
            Solution, Spec, AsJsonDocument, WorkingDirectory, BypassPersistedCache, NoBinlogReplay,
            ReportPartialModels);
    }

    /// <summary>
    ///     The <c>arch_explain</c> run. The verb has no document, replay or partial-model option, so only the
    ///     persisted-cache leg applies.
    /// </summary>
    /// <exception cref="UserErrorException">Not exactly one of the three is given.</exception>
    internal ExplainRequest ExplainRequest(string? ruleId, string? type, string? path)
    {
        // An empty string counts as absent rather than as a rule ID, type or path that names nothing, so a caller
        // that fills every optional argument with one still names a single form.
        string? id = Given(ruleId);
        string? typeName = Given(type);
        string? pathValue = Given(path);
        int forms = new[] { id, typeName, pathValue }.Count(form => form is not null);
        if (forms != 1) throw new UserErrorException(ExplainFormsRefusal);

        return new ExplainRequest(id, Solution, Spec, WorkingDirectory, BypassPersistedCache, typeName, pathValue);
    }

    internal const string ExplainFormsRefusal = "Pass exactly one of ruleId, type and path.";

    /// <summary>
    ///     An optional string argument, with an empty string read as absent: some clients fill every optional
    ///     argument with one.
    /// </summary>
    private static string? Given(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>The <c>arch_context</c> run, a card lookup, so none of the policy legs applies.</summary>
    internal ContextRequest ContextRequest(string path)
    {
        return new ContextRequest(path, Solution, Spec, WorkingDirectory);
    }

    /// <summary>
    ///     The <c>arch_graph</c> run. Two departures from the policy above, both deliberate:
    ///     <see cref="Spec" /> goes unused, because the survey is a property of the codebase and derive runs
    ///     before any spec exists; and <paramref name="allowWorkspaceDiagnostics" /> is the caller's, because
    ///     graph refuses before it has a document to stamp, so opting into a partial model is a choice the
    ///     tool exposes rather than one the surface makes.
    /// </summary>
    internal GraphRequest GraphRequest(bool allowWorkspaceDiagnostics, DocumentGrain grain, string? projects)
    {
        return new GraphRequest(
            Solution, AsJsonDocument, WorkingDirectory, BypassPersistedCache, NoBinlogReplay,
            allowWorkspaceDiagnostics, grain, projects);
    }
}
