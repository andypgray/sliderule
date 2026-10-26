using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Cli;
using SlideRule.Tests.Mcp.TestDoubles;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     What one cache-wired verb run produced: the CLI result (exit code, both output channels and the
///     workspace loads), how the run obtained its codebase, which projects it re-extracted, how many times it
///     walked the workspace for fragments, and the counting source every acquisition went through.
/// </summary>
internal sealed record CacheRun(
    CliResult Result,
    CodebaseSourceOutcome? Outcome,
    IReadOnlySet<string> ReExtracted,
    int ExtractionCount,
    CountingSolutionSource Source)
{
    /// <summary>How many times the run asked for a loaded solution, closures included.</summary>
    public int AcquireCount => Source.AcquireCount;
}

/// <summary>
///     Runs a cache-wired verb in process over a persisted cache rooted at a caller-supplied directory, through a
///     <see cref="CountingSolutionSource" /> the result hands back. <see cref="CheckAsync" /> and
///     <see cref="GraphAsync" /> build the JSON-mode request for their verb; any other verb passes its runner and
///     its request to <see cref="RunAsync{TRunner}" />.
/// </summary>
internal static class CacheRuns
{
    /// <summary>
    ///     One <c>check --json</c> run of <paramref name="spec" /> over <paramref name="solution" />. A null
    ///     <paramref name="spec" /> leaves the spec to discovery; a null <paramref name="source" /> counts over a
    ///     cold source.
    /// </summary>
    internal static Task<CacheRun> CheckAsync(
        string solution, string? spec, string cacheRoot, bool noCache = false, string? sarif = null,
        CountingSolutionSource? source = null)
    {
        CheckRequest request = CheckRequests.For(solution, spec, SolutionPaths.SolutionDirectoryOf(solution))
            with
            {
                Json = true, NoCache = noCache, Sarif = sarif
            };

        return RunAsync(
            cacheRoot,
            static (output, error, counting, environment) => new CheckRunner(output, error, counting, environment),
            runner => runner.RunAsync(request, Ct),
            source);
    }

    /// <summary>
    ///     One <c>graph --json</c> run over <paramref name="solution" />. A null <paramref name="source" /> counts
    ///     over a cold source.
    /// </summary>
    internal static Task<CacheRun> GraphAsync(
        string solution, string cacheRoot, bool noCache = false, CountingSolutionSource? source = null)
    {
        GraphRequest request = GraphRequests.For(solution, SolutionPaths.SolutionDirectoryOf(solution))
            with
            {
                Json = true, NoCache = noCache
            };

        return RunAsync(
            cacheRoot,
            static (output, error, counting, environment) => new GraphRunner(output, error, counting, environment),
            runner => runner.RunAsync(request, Ct),
            source);
    }

    /// <summary>
    ///     One run of the runner <paramref name="create" /> builds over a fresh writer pair, the counting source
    ///     (<paramref name="source" />, or a cold one when null) and an environment whose cache root is
    ///     <paramref name="cacheRoot" />, started by <paramref name="run" />. The runner's cache observables are
    ///     read once the run has finished.
    /// </summary>
    internal static async Task<CacheRun> RunAsync<TRunner>(
        string cacheRoot,
        Func<TextWriter, TextWriter, ISolutionSource, IEnvironment, TRunner> create,
        Func<TRunner, Task<int>> run,
        CountingSolutionSource? source = null)
        where TRunner : CacheWiredRunner
    {
        CountingSolutionSource counting = source ?? new CountingSolutionSource();
        FakeEnvironment environment = FakeEnvironment.WithCacheRoot(cacheRoot);
        TRunner? runner = null;

        CliResult result = await CliResult.CapturedAsync((output, error) =>
        {
            runner = create(output, error, counting, environment);
            return run(runner);
        });

        return new CacheRun(
            result, runner!.LastOutcome, runner.LastReExtractedProjects, runner.LastExtractionCount, counting);
    }
}
