using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;

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
}
