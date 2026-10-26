using Shouldly;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end tests for the one place <c>baseline</c>'s persisted-cache policy is decided: the mode,
///     not the flag. Over a cache slot a <c>graph</c> run has already populated, <c>init</c> and
///     <c>accept-reductions</c> extract cache-free and open a workspace even though the caller asked for
///     the cache, while <c>add</c> on the same slot answers from it with no workspace at all.
/// </summary>
/// <remarks>
///     <para>
///         Both modes read absence as evidence — <c>init</c> captures "zero debt" for an uncaptured rule
///         and <c>accept-reductions</c> deletes entries whose violation no longer occurs — so a
///         smaller-than-real model makes them write a wrong conclusion into a file that outlives the run.
///         The incomplete-model gate and the narrowing refusal already stop the other two routes to that
///         model; a stale cache is the third and the only silent one, because a hit replays the recorded
///         diagnostics and so raises nothing either gate could fire on. That is why the refusal has to live
///         on the mode rather than on the flag, and why every request below asks for the cache: a
///         <see cref="CodebaseSourceOutcome.Disabled" /> verdict on a request that said
///         <c>NoCache: false</c> is the whole claim.
///     </para>
/// </remarks>
public sealed class BaselineCacheE2ETests
{
    private const string InlineSqlRule = "data-access/no-inline-sql";

    // A currently observed violation of the fixture's committed Migrate baseline, so add resolves it and
    // updates its attribution rather than needing a fresh red to grandfather.
    private const string CapturedSource = "MyApp.Web.HomeController";
    private const string CapturedTarget = "System.Data.DataTable";

    [Fact]
    public async Task Baseline_OverAPopulatedSlot_TheTwoAbsenceReadingModesExtractCacheFreeAndAddHits()
    {
        using var workspace = new TempFixtureWorkspace();
        using var cache = new TempCacheRoot("baseline-cache");

        // Populate the slot: graph has no spec and no exclusion, so it writes every project's fragments —
        // exactly the set a spec-ful hit merges from.
        CacheRun populate = await CacheRuns.GraphAsync(
            workspace.SolutionPath, cache.Root, source: CountingSolutionSource.OverWarmPool());
        populate.Outcome.ShouldBe(CodebaseSourceOutcome.Miss);

        // add first, so the hit below cannot be an artefact of what the two write-happy modes leave on disk.
        CacheRun add = await RunBaselineAsync(workspace.SolutionPath, cache.Root, AddRequest);
        CacheRun init = await RunBaselineAsync(workspace.SolutionPath, cache.Root, BaselineRequests.Init);
        CacheRun accept = await RunBaselineAsync(workspace.SolutionPath, cache.Root, BaselineRequests.AcceptReductions);

        // add records one violation this run saw, so it rides the cache: no workspace, no design-time build.
        add.Outcome.ShouldBe(CodebaseSourceOutcome.Hit);
        add.AcquireCount.ShouldBe(0);
        add.Result.ShouldSucceed();

        // The other two asked for the cache and were refused it by their mode — the gate this class exists for.
        init.Outcome.ShouldBe(CodebaseSourceOutcome.Disabled);
        init.AcquireCount.ShouldBe(1);
        accept.Outcome.ShouldBe(CodebaseSourceOutcome.Disabled);
        accept.AcquireCount.ShouldBe(1);
    }

    [Fact]
    public async Task BaselineAdd_WithNoCache_IsTheOperatorsToTurnOffAgain()
    {
        using var workspace = new TempFixtureWorkspace();
        using var cache = new TempCacheRoot("baseline-cache");

        CacheRun populate = await CacheRuns.GraphAsync(
            workspace.SolutionPath, cache.Root, source: CountingSolutionSource.OverWarmPool());
        populate.Outcome.ShouldBe(CodebaseSourceOutcome.Miss);

        // The mode gate only ever forces the cache OFF, so on add the flag is the whole policy: the same
        // slot that produced a hit above produces a cold run here.
        CacheRun add = await RunBaselineAsync(
            workspace.SolutionPath,
            cache.Root,
            workingDirectory => AddRequest(workingDirectory) with { NoCache = true });

        add.Outcome.ShouldBe(CodebaseSourceOutcome.Disabled);
        add.AcquireCount.ShouldBe(1);
        add.Result.ShouldSucceed();
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static BaselineRequest AddRequest(string workingDirectory)
    {
        return BaselineRequests.Add(InlineSqlRule, "INC-1234", CapturedSource, CapturedTarget, workingDirectory);
    }

    private static Task<CacheRun> RunBaselineAsync(
        string solution, string cacheRoot, Func<string, BaselineRequest> mode)
    {
        // NoCache false on every mode deliberately (the factories leave the cache on): what each run reports
        // is then the mode's own answer.
        BaselineRequest request = mode(SolutionPaths.SolutionDirectoryOf(solution))
            with
            {
                Solution = solution,
                Spec = CliRunner.ViolatedSpecDll
            };

        return CacheRuns.RunAsync(
            cacheRoot,
            static (output, error, counting, environment) => new BaselineRunner(output, error, counting, environment),
            runner => runner.RunAsync(request, Ct),
            CountingSolutionSource.OverWarmPool());
    }
}
