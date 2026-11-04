using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>graph</c> verb: a survey of the whole codebase, as text, JSON or a Mermaid diagram, through a spec-less
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
///     <para>
///         The Mermaid format draws what <c>render --diagram</c> draws for the same project globs, so
///         <see cref="GraphRequest.Projects" /> scopes it as <c>--diagram-only</c> does: declared projects only,
///         and an edge only when both its ends are drawn. What the human run would stamp about the load rides the
///         fence as comments, because on MCP the fence is the only channel. It has no grain to degrade, so a fence
///         over the caller's budget refuses whole rather than reach the truncator.
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
        TextWriter human = HumanChannel(request.Format != GraphFormat.Text, output);

        (CodebaseSource survey, GraphSummary summary) = await SurveyAsync(
            request.Solution, request.WorkingDirectory, request.NoCache, request.AllowWorkspaceDiagnostics, ct);
        using CodebaseSource source = survey;
        string solutionName = source.SolutionName;

        // Scope, then refuse, then render. The refusal fires here rather than at parse time because the
        // inventory it lists IS the extraction's output: nothing before this point knows the solution's
        // project names, so a filter that matches nothing cannot be caught any earlier.
        IReadOnlyList<string> projectGlobs = GlobList.Parse(request.Projects);
        GraphSummary scoped = GraphSummarizer.Scope(summary, projectGlobs);
        if (projectGlobs.Count > 0 && scoped.Projects.Count == 0)
            throw new UserErrorException(UnmatchedProjectsMessage(projectGlobs, summary));

        // --json purity: only the JSON document reaches stdout; workspace diagnostics go to stderr. Composed
        // once for both, so the survey document carries the MSBuild-selection note the survey's incomplete-model
        // refusal carries.
        IReadOnlyList<string> renderedDiagnostics = source.Diagnostics.Rendered;

        // The stamp goes the same way: read off the load once, then handed to stderr, to the human survey
        // and to every rung of the document ladder, so none of the three regroups the load's advisories or
        // respells its project paths for itself.
        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(source);
        WorkspaceDiagnosticsRenderer.Render(error, trust, renderedDiagnostics, source, request.Json);

        // The narrowing stamp is human-channel only, where the document carries the same fact in
        // uncheckedProjects.
        NarrowingNotices.Stamp(human, source, NarrowedUniverseNotice.GraphStamp);

        switch (request.Format)
        {
            case GraphFormat.Json:
                WriteJson(request, scoped, solutionName, renderedDiagnostics, trust, projectGlobs);
                break;
            case GraphFormat.Mermaid:
                WriteMermaid(summary, solutionName, projectGlobs, source);
                break;
            default:
                WriteHuman(request, summary, scoped, solutionName, projectGlobs, trust);
                break;
        }

        return ExitCodes.Clean;
    }

    // The JSON survey, each rung re-composed from the summary already in hand, no second extraction.
    // Degrading coarsens the grain and never touches the scope: every rung carries the same narrowing.
    private void WriteJson(
        GraphRequest request, GraphSummary scoped, string solutionName,
        IReadOnlyList<string> renderedDiagnostics, WorkspaceTrustStamp trust,
        IReadOnlyList<string> projectGlobs)
    {
        WriteFitted(output, request.Grain, at => GraphJsonRenderer.Document(
            scoped, solutionName, renderedDiagnostics, trust, at, projectGlobs));
    }

    // The unscoped summary, because the fence scopes its own nodes and draws an edge only between two of them,
    // which is what keeps it byte-identical to render's for the same globs. The terminator is written as LF, so
    // the bytes match on every platform.
    private void WriteMermaid(
        GraphSummary summary, string solutionName, IReadOnlyList<string> projectGlobs, CodebaseSource source)
    {
        string fence = GraphDiagramRenderer.Fence(
            summary, solutionName, new DiagramScope(projectGlobs, []), DiagramComments(source)) + "\n";

        output.Write(Fitter.FitWhole(fence, GraphFormats.OversizedRefusal));
    }

    // Each fact the human run stamps about the load, one comment line per line of its stamp, in the order the
    // human run writes them. A clean load has none.
    private static IReadOnlyList<string> DiagramComments(CodebaseSource source)
    {
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        var blocks = new List<string>();

        if (diagnostics.IsIncomplete) blocks.Add(IncompleteModelGate.DiagramCaveat(diagnostics));
        if (NarrowingNotices.Text(source, NarrowedUniverseNotice.DiagramStamp) is { } narrowed) blocks.Add(narrowed);
        if (UnsupportedProjectsNotices.Text(source, UnsupportedProjectsNotice.DiagramStamp) is { } unsupported)
            blocks.Add(unsupported);

        return blocks.SelectMany(block => block.Split('\n'))
            .ToList();
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
