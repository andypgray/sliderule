using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Mcp.TestDoubles;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     What one cache-wired verb run produced: its exit code and both output channels, how the run obtained
///     its codebase, which projects it re-extracted, and the counting source every acquisition went through.
/// </summary>
internal sealed record CacheRun(
    int Exit,
    string Out,
    string Err,
    CodebaseSourceOutcome? Outcome,
    IReadOnlySet<string> ReExtracted,
    CountingSolutionSource Source)
{
    /// <summary>How many times the run asked for a loaded solution, closures included.</summary>
    public int AcquireCount => Source.AcquireCount;
}

/// <summary>
///     Runs <c>check</c> and <c>graph</c> in process over a persisted cache rooted at a caller-supplied
///     directory, in JSON mode, through a <see cref="CountingSolutionSource" /> the result hands back.
/// </summary>
internal static class CacheRuns
{
    /// <summary>
    ///     One <c>check --json</c> run of <paramref name="spec" /> over <paramref name="solution" />. A null
    ///     <paramref name="spec" /> leaves the spec to discovery; a null <paramref name="source" /> counts over a
    ///     cold source.
    /// </summary>
    internal static async Task<CacheRun> CheckAsync(
        string solution, string? spec, string cacheRoot, bool noCache = false, string? sarif = null,
        CountingSolutionSource? source = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        CountingSolutionSource counting = source ?? new CountingSolutionSource();
        var runner = new CheckRunner(output, error, counting, FakeEnvironment.WithCacheRoot(cacheRoot));

        CheckRequest request = CheckRequests.For(solution, spec, SolutionPaths.SolutionDirectoryOf(solution))
            with
            {
                Json = true, NoCache = noCache, Sarif = sarif
            };

        int exit = await runner.RunAsync(request, Ct);

        return new CacheRun(
            exit, output.ToString(), error.ToString(), runner.LastOutcome, runner.LastReExtractedProjects, counting);
    }

    /// <summary>One <c>graph --json</c> run over <paramref name="solution" />.</summary>
    internal static async Task<CacheRun> GraphAsync(string solution, string cacheRoot, bool noCache = false)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var counting = new CountingSolutionSource();
        var runner = new GraphRunner(output, error, counting, FakeEnvironment.WithCacheRoot(cacheRoot));

        GraphRequest request = GraphRequests.For(solution, SolutionPaths.SolutionDirectoryOf(solution))
            with
            {
                Json = true, NoCache = noCache
            };

        int exit = await runner.RunAsync(request, Ct);

        return new CacheRun(
            exit, output.ToString(), error.ToString(), runner.LastOutcome, runner.LastReExtractedProjects, counting);
    }
}
