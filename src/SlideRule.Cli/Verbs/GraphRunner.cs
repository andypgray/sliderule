using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>graph</c> verb: a survey of the whole codebase, human or JSON, through a spec-less
///     <see cref="CodebaseSource" /> fronted by the persisted extraction cache.
/// </summary>
/// <remarks>
///     <para>
///         <b>What it refuses.</b> What the codebase <em>contains</em> never gates — the survey exits 0
///         however alarming the graph is. What gates is the survey not being of the codebase: a project that
///         failed to load is missing from the map entirely, so the run refuses before extraction on
///         <c>check</c>'s terms, opt-out <see cref="GraphRequest.AllowWorkspaceDiagnostics" />
///         (<see cref="IncompleteModelGate" />). That refusal, like a discovery/workspace failure, surfaces
///         as a <see cref="UserErrorException" /> — exit 2 on the CLI, an error result on MCP.
///     </para>
///     <para>
///         Two knobs narrow what a caller reads, and they are independent: <see cref="GraphRequest.Projects" />
///         narrows the <em>subject</em> (which projects the survey is of), while
///         <see cref="GraphRequest.Grain" /> coarsens the <em>grain</em> (how much detail each one gets).
///         A filter matching no project refuses with the available names rather than surveying nothing —
///         or, where the filter is a JSON array written as text, names that shape instead
///         (<see cref="Refusals" />).
///     </para>
/// </remarks>
internal sealed class GraphRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource source,
    IEnvironment environment,
    IResponseFitter? fitter = null) : CacheWiredRunner(source, environment, fitter)
{
    public async Task<int> RunAsync(GraphRequest request, CancellationToken ct)
    {
        // This run's human channel. --json owns stdout, where the survey document is the only thing written,
        // so under it the stamp below goes nowhere.
        TextWriter human = request.Json ? TextWriter.Null : output;

        using var source = await CodebaseSource.CreateSpeclessAsync(
            SolutionSource, Environment, request.Solution, request.WorkingDirectory, request.NoCache, ct);

        // Refuse before extraction, not after: a survey of a partial model is a wrong map, not a smaller one.
        // The refusal carries the diagnostics in its own text rather than leaving them to the stderr render
        // below, so the MCP surface — which discards this error writer — gets the same actionable message.
        // A cache hit refuses identically: diagnostics persist into the extraction cache and replay with it.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        if (diagnostics.Gates(request.AllowWorkspaceDiagnostics))
            throw new UserErrorException(IncompleteModelGate.GraphRefusal(diagnostics));

        CodebaseModel codebase = await source.ExtractAsync([], ct); // spec-less: the survey excludes nothing
        RecordCacheOutcome(source);
        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        string solutionName = source.SolutionName;

        // Scope, then refuse, then render. The refusal fires here rather than at parse time because the
        // inventory it lists IS the extraction's output: nothing before this point knows the solution's
        // project names, so a filter that matches nothing cannot be caught any earlier.
        IReadOnlyList<string> projectGlobs = GlobList.Parse(request.Projects);
        GraphSummary scoped = GraphSummarizer.Scope(summary, projectGlobs);
        if (projectGlobs.Count > 0 && scoped.Projects.Count == 0)
            throw new UserErrorException(UnmatchedProjectsMessage(projectGlobs, summary));

        // --json purity: only the JSON document reaches stdout; workspace diagnostics go to stderr. Composed
        // once for both, so the survey document carries the MSBuild-selection note the refusal above already
        // carries.
        IReadOnlyList<string> renderedDiagnostics = diagnostics.Rendered;

        // The stamp goes the same way: read off the load once, then handed to stderr, to the human survey
        // and to every rung of the document ladder, so none of the three regroups the load's advisories or
        // respells its project paths for itself.
        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(diagnostics, source.SolutionDirectory);
        WorkspaceDiagnosticsRenderer.Render(error, trust, renderedDiagnostics, request.Json);

        // The narrowing stamp is human-channel only, where the document carries the same fact in
        // uncheckedProjects.
        NarrowingNotices.Stamp(human, source, NarrowedUniverseNotice.GraphStamp);

        if (request.Json)
            WriteJson(request, scoped, solutionName, renderedDiagnostics, trust, projectGlobs);
        else
            WriteHuman(request, summary, scoped, solutionName, projectGlobs, trust);

        return ExitCodes.Clean;
    }

    // The JSON survey, degraded rather than cut (IResponseFitter). Each rung is re-composed from the summary
    // already in hand, no second extraction, so it is byte-identical to what --grain naming it would write,
    // and carries its terminator, so the fitter measures the text the MCP truncator measures after it.
    // Degrading coarsens the grain and never touches the scope: every rung carries the same narrowing.
    private void WriteJson(
        GraphRequest request, GraphSummary scoped, string solutionName,
        IReadOnlyList<string> renderedDiagnostics, WorkspaceTrustStamp trust,
        IReadOnlyList<string> projectGlobs)
    {
        IEnumerable<string> ladder = request.Grain.Ladder(Compose);
        string document = Fitter.Fit(ladder);
        output.Write(document);
        return;

        string Compose(DocumentGrain at)
        {
            string composed = GraphJsonRenderer.Document(
                scoped, solutionName, renderedDiagnostics, trust, at, projectGlobs);
            return SlideRuleJson.Terminated(composed);
        }
    }

    // The human survey. The scope stamp is written here rather than by the formatter so an unscoped run writes
    // the formatter's output alone, and so the stamp can say what the formatter cannot see: how much of the
    // solution this covers, and why an edge below may name a project the roster does not.
    private void WriteHuman(
        GraphRequest request, GraphSummary summary, GraphSummary scoped, string solutionName,
        IReadOnlyList<string> projectGlobs, WorkspaceTrustStamp trust)
    {
        if (projectGlobs.Count > 0)
        {
            output.WriteLine(ScopeStamp(projectGlobs, summary, scoped));
            output.WriteLine();
        }

        // The same stamp the --json document is composed from, so the terminal reader and the document
        // cannot disagree about which projects the survey could not read, or how they are spelled.
        foreach (string line in GraphFormatter.Lines(
                     scoped, solutionName, request.Grain, trust.UnsupportedProjects ?? []))
            output.WriteLine(line);
    }

    private static string ScopeStamp(IReadOnlyList<string> projectGlobs, GraphSummary summary, GraphSummary scoped)
    {
        return $"Scoped to {scoped.Projects.Count} of {summary.Projects.Count} projects matching "
               + $"'{string.Join(";", projectGlobs)}'; references in both directions are kept, so an edge below "
               + "can name a project outside the scope.";
    }

    // The unmatched-filter refusal, in the shared shape explain's unknown-rule refusal also takes. The
    // roster rides in survey order, not sorted: it is the summary's own order, which is what the rest of
    // the survey prints. A JSON array a client wrote into the string parameter gets the shape refusal
    // instead — the same one check's rule filter gives, because the two filters take the same list.
    private static string UnmatchedProjectsMessage(IReadOnlyList<string> projectGlobs, GraphSummary summary)
    {
        string written = string.Join(";", projectGlobs);
        var lead = $"No project matched '{written}'";

        return Refusals.StringifiedArrayRefusal(written, lead, Refusals.GlobListAdvice)
               ?? Refusals.NotFoundMessage(lead, "Available projects", summary.Projects.Select(project => project.Name));
    }
}
