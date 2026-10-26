using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.SpecLoading;
using SlideRule.Hosting;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The base of every verb that fronts the persisted extraction cache: the <see cref="IEnvironment" /> seam the
///     cache root is read through, and the observables a test reads to learn which path a run took.
/// </summary>
/// <remarks>
///     The observables are never printed: stdout and stderr are byte-identical whichever path a run takes, which is
///     the whole claim the cache has to earn. The base class does not decide the policy: each runner spells its own
///     as the <c>noCache</c> argument it passes <see cref="CodebaseSource.CreateWithSpecAsync" />.
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
    }

    /// <summary>
    ///     A run's human channel: <paramref name="stdout" />, or nowhere under <c>--json</c>, which owns stdout,
    ///     where the document is the only thing written, so every stamp and notice goes nowhere.
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
    ///     The spec's model alone, for a verb that reads the spec and never the code, with the solution directory
    ///     when <paramref name="needsSolutionDirectory" /> asks for it (null otherwise on the built-DLL path).
    /// </summary>
    /// <remarks>
    ///     A built-DLL spec resolves without ever opening the solution. The model still comes through the source,
    ///     so a warm host serves its cached one rather than reloading the DLL per call. When the directory is
    ///     wanted the solution is discovered first, so a missing solution is the error reported before a bad DLL.
    ///     A convention or csproj spec loads the workspace for resolution only and never extracts, through the
    ///     shared acquisition seam, so the model is the model every other verb would have loaded; its diagnostics
    ///     are composed like every other verb's, so the MSBuild-selection note accompanies the load failures.
    /// </remarks>
    protected async Task<(ArchitectureModel Model, string? SolutionDirectory)> LoadSpecModelAsync(
        string? solution, string? spec, string workingDirectory, bool noCache, bool needsSolutionDirectory,
        TextWriter error, CancellationToken ct)
    {
        SpecResolution? withoutSolution = SpecResolver.TryResolveWithoutSolution(spec);
        if (withoutSolution is not null)
        {
            string? anchor = null;
            if (needsSolutionDirectory)
            {
                string solutionPath = ModelPipeline.DiscoverSolution(solution, workingDirectory, Environment);
                anchor = SolutionProjectFileParser.AnchorDirectory(solutionPath);
            }

            ArchitectureModel model = SolutionSource.LoadSpecModel(withoutSolution.DllPath);
            return (model, anchor);
        }

        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, solution, spec, workingDirectory, noCache, ct);
        WorkspaceDiagnosticsRenderer.Render(error, source.Diagnostics);
        RecordCacheOutcome(source);

        return (source.Model, source.SolutionDirectory);
    }
}
