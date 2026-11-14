using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.SpecLoading;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The base of every verb that fronts the persisted extraction cache: the <see cref="IEnvironment" /> seam the
///     cache root is read through, and the observables a test reads to learn which path a run took.
/// </summary>
/// <remarks>
///     Stdout and stderr are byte-identical whichever path a run takes, which is the whole claim the cache has to
///     earn, so the observables reach the terminal only under <c>check --timings</c>, as its cache line on stderr.
///     The base class does not decide the policy: each runner spells its own as the <c>noCache</c> argument it
///     passes the <see cref="CodebaseSource" /> it reads through.
/// </remarks>
internal abstract class CacheWiredRunner(
    ISolutionSource source,
    IEnvironment environment,
    IResponseFitter? fitter = null)
    : WorkspaceRunner(source)
{
    protected IEnvironment Environment { get; } = environment;

    protected IResponseFitter Fitter { get; } = fitter ?? ResponseFitter.FirstRung;

    /// <summary>The last run's <see cref="CodebaseSource.Outcome" />.</summary>
    internal CodebaseSourceOutcome? LastOutcome { get; private set; }

    /// <summary>The last run's <see cref="CodebaseSource.ReExtractedProjects" />.</summary>
    internal IReadOnlySet<string> LastReExtractedProjects { get; private set; } = new HashSet<string>();

    /// <summary>The last run's <see cref="CodebaseSource.ExtractionCount" />.</summary>
    internal int LastExtractionCount { get; private set; }

    /// <summary>
    ///     Publishes the cache observables off a completed run's source. Called after the pipeline, because
    ///     the re-extraction set and the walk count are only final once every
    ///     <see cref="CodebaseSource.ExtractAsync" /> the run makes has run.
    /// </summary>
    protected void RecordCacheOutcome(CodebaseSource source)
    {
        LastOutcome = source.Outcome;
        LastReExtractedProjects = source.ReExtractedProjects;
        LastExtractionCount = source.ExtractionCount;
        PhaseTimings.RecordCache(new CacheVerdict(source.Outcome, source.ExtractedProjectCount, source.ProjectCount));
    }

    /// <summary>
    ///     A run's human channel: <paramref name="stdout" />, or nowhere under <c>--json</c>, where the document is
    ///     the only thing written to stdout.
    /// </summary>
    protected static TextWriter HumanChannel(bool json, TextWriter stdout)
    {
        return json ? TextWriter.Null : stdout;
    }

    /// <summary>
    ///     Writes a JSON document degraded rather than cut (<see cref="IResponseFitter" />): each rung from
    ///     <paramref name="floor" /> up is composed by <paramref name="compose" /> from what the run already holds,
    ///     so it is byte-identical to what <c>--grain</c> naming it would write, and carries its terminator, so the
    ///     fitter measures the text the MCP truncator measures after it.
    /// </summary>
    protected void WriteFitted(TextWriter stdout, DocumentGrain floor, Func<DocumentGrain, string> compose)
    {
        IEnumerable<string> ladder = floor.Ladder(at => SlideRuleJson.Terminated(compose(at)));
        string document = Fitter.Fit(ladder);
        stdout.Write(document);
    }

    /// <summary>
    ///     The spec-less survey of the whole solution, with the source it was read from, which the caller disposes.
    /// </summary>
    /// <exception cref="UserErrorException">
    ///     A project failed to load or to restore and <paramref name="allowWorkspaceDiagnostics" /> is false.
    /// </exception>
    protected async Task<(CodebaseSource Source, GraphSummary Summary)> SurveyAsync(
        string? solution, string workingDirectory, bool noCache, bool allowWorkspaceDiagnostics, CancellationToken ct)
    {
        var source = await CodebaseSource.CreateSpeclessAsync(
            SolutionSource, Environment, solution, workingDirectory, noCache, ct);
        try
        {
            // Refuse before extraction: a survey of a partial model shows each project that failed to load as one
            // the solution does not have.
            // The refusal carries the diagnostics in its own text rather than leaving them to a stderr render, so
            // the MCP surface, which discards the error writer, gets the same actionable message. A cache hit
            // refuses identically: diagnostics persist into the extraction cache and replay with it.
            WorkspaceDiagnostics diagnostics = source.Diagnostics;
            if (diagnostics.Gates(allowWorkspaceDiagnostics))
                throw new UserErrorException(IncompleteModelGate.GraphRefusal(diagnostics));

            CodebaseModel codebase = await source.ExtractAsync([], ct); // spec-less: the survey excludes nothing
            RecordCacheOutcome(source);
            return (source, GraphSummarizer.Summarize(codebase));
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     The spec's model alone, for a verb that reads the spec and never the code, with the solution directory
    ///     (null only on the built-DLL path when no solution is found and <paramref name="needsSolutionDirectory" />
    ///     did not require one), and the spec's <see cref="CodebaseSource.StaleSpec" /> verdict; a verdict that is
    ///     not null comes with a solution directory that is not null either.
    /// </summary>
    /// <remarks>
    ///     A missing DLL is refused first and the solution is discovered next, so a missing solution is reported
    ///     before a DLL that fails to load. A DLL spec with no solution found is loaded as given and nothing is
    ///     compared; every other path goes through <see cref="CodebaseSource.CreateSpecOnlyAsync" />, and its load
    ///     diagnostics and stale-spec line render as every other verb's do.
    /// </remarks>
    protected async Task<(ArchitectureModel Model, string? SolutionDirectory, StaleSpec? StaleSpec)> LoadSpecModelAsync(
        string? solution, string? spec, string workingDirectory, bool noCache, bool needsSolutionDirectory,
        TextWriter error, CancellationToken ct)
    {
        SpecResolution? dll = SpecResolver.TryResolveWithoutSolution(spec);
        string? solutionPath = dll is null || needsSolutionDirectory
            ? ModelPipeline.DiscoverSolution(solution, workingDirectory, Environment)
            : TryDiscoverSolution(solution, workingDirectory);
        if (solutionPath is null) return (SolutionSource.LoadSpecModel(dll!.DllPath), null, null);

        using var source = await CodebaseSource.CreateSpecOnlyAsync(
            SolutionSource, Environment, solutionPath, spec, noCache, ct);
        WorkspaceDiagnosticsRenderer.Render(error, source);
        RecordCacheOutcome(source);

        return (source.Model, source.SolutionDirectory, source.StaleSpec);
    }

    // Discovery for a verb that answers without a solution when none is found.
    private string? TryDiscoverSolution(string? solution, string workingDirectory)
    {
        try
        {
            return ModelPipeline.DiscoverSolution(solution, workingDirectory, Environment);
        }
        catch (UserErrorException)
        {
            return null;
        }
    }
}
