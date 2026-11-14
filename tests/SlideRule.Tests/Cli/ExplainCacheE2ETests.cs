using Shouldly;
using SlideRule.Cli.Pipeline;
using SlideRule.Roslyn.Caching;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     <c>explain</c> against the persisted extraction cache, over the one fixture that declares a spec
///     project (<see cref="LayoutAppFixture" />) and therefore the only bed where explain resolves by
///     convention against a real workspace. A spec passed as its built DLL reads the cache only for the record
///     naming the project that builds it, which <see cref="StaleSpecE2ETests" /> covers.
/// </summary>
/// <remarks>
///     <para>
///         Explain is the verb the cache does the most for and the only one that consumes a slot without
///         ever filling it. It never calls <c>ExtractAsync</c> — the model it dumps is the spec's — so it
///         reaches neither the fingerprint nor the write-back, and a miss leaves the root empty. What a hit
///         spares it is correspondingly the whole of its cost rather than a share of it: the recorded
///         resolution replays, the model loads from the DLL it names, and no design-time build runs. A partial
///         slot serves it as well as a hit, because the record holds only structural facts and an edit to a
///         source is never a structural change.
///     </para>
///     <para>
///         Every run is by convention rather than <c>--spec</c>, because the recorded resolution is keyed on
///         the normalized spec argument: the filling run and the reading run have to spell it the same way
///         or the hit falls back to a cold resolve and the fact would pass for the wrong reason.
///     </para>
/// </remarks>
public sealed class ExplainCacheE2ETests
{
    private const string RuleId = LayoutAppFixture.RuleId;

    [Fact]
    public async Task Explain_OnASlotAnotherVerbFilled_AnswersWithNoWorkspaceAndIsByteIdentical()
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("explain-cache");

        CliResult cold = await ExplainAsync(workspace, cache.Root);

        cold.ShouldSucceed($"{RuleId} (enforce)");
        cold.Loads.ShouldBe(1); // a convention spec resolves against the solution, so a miss opens one
        cache.HasCacheFile()
            .ShouldBeFalse(); // and explain, which never extracts, has nothing to write back

        // A verb that does extract fills the slot — and records this spec's resolution in it, which is what
        // the hit below replays instead of re-resolving against a workspace it no longer opens.
        CliResult check = await RunAsync(cache.Root, "check", workspace.SolutionPath);
        check.ShouldSucceed($"pass {RuleId}");
        check.Loads.ShouldBe(1);
        cache.HasCacheFile()
            .ShouldBeTrue();

        CliResult cached = await ExplainAsync(workspace, cache.Root);

        cached.Loads.ShouldBe(0); // the whole of what a convention --spec costs, skipped
        cached.ShouldMatchTheOutputOf(cold);
    }

    [Fact]
    public async Task Explain_OnAPartialSlot_AnswersWithNoWorkspaceAndIsByteIdentical()
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("explain-cache");

        CliResult cold = await ExplainAsync(workspace, cache.Root);
        CliResult check = await RunAsync(cache.Root, "check", workspace.SolutionPath);
        check.ShouldSucceed($"pass {RuleId}");

        // An edit to a source outside the spec project leaves the slot partial, which no full validation serves.
        FixtureEdits.InsertMember(workspace.PathOf("LayoutApp.Core", "Ledger.cs"), "\n    private const int Unread = 0;\n");
        new ExtractionCacheStore(workspace.SolutionPath, cache.Root).ReadAndValidate(Ct)
            .Outcome.ShouldBe(CacheOutcome.Partial);

        CliResult partial = await ExplainAsync(workspace, cache.Root);

        partial.Loads.ShouldBe(0);
        partial.ShouldMatchTheOutputOf(cold);
    }

    [Fact]
    public async Task Explain_OnAPartialSlotWithNoRecordForItsSpec_LoadsTheWholeSolutionAndWritesNothing()
    {
        // graph fills the slot and records no spec, so the partial below holds nothing explain can replay.
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("explain-cache");
        CacheRun graph = await CacheRuns.GraphAsync(workspace.SolutionPath, cache.Root);
        graph.Result.Exit.ShouldBe(0, graph.Result.Transcript());
        FixtureEdits.InsertMember(workspace.PathOf("LayoutApp.Core", "Ledger.cs"), "\n    private const int Unread = 0;\n");
        new ExtractionCacheStore(workspace.SolutionPath, cache.Root).ReadAndValidate(Ct)
            .Outcome.ShouldBe(CacheOutcome.Partial);
        string cacheFile = CacheLocations.CacheFilePath(workspace.SolutionPath, cache.Root);
        byte[] written = File.ReadAllBytes(cacheFile);

        CacheRun explain = await CacheRuns.ExplainAsync(workspace.SolutionPath, RuleId, null, cache.Root);

        explain.Result.ShouldSucceed($"{RuleId} (enforce)");
        (explain.Outcome, explain.AcquireCount, explain.Source.ClosureRequests.Count)
            .ShouldBe((CodebaseSourceOutcome.Miss, 1, 0));
        File.ReadAllBytes(cacheFile)
            .ShouldBe(written);
    }

    [Fact]
    public async Task Explain_NoCache_StaysColdOverAFilledSlot()
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("explain-cache");

        await RunAsync(cache.Root, "check", workspace.SolutionPath);

        CliResult result = await ExplainAsync(workspace, cache.Root, "--no-cache");

        result.ShouldSucceed($"{RuleId} (enforce)");
        result.Loads.ShouldBe(1); // the flag keeps explain on the cold path, resolving against a workspace
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static Task<CliResult> ExplainAsync(
        TempFixtureWorkspace workspace, string cacheRoot, params string[] extra)
    {
        return RunAsync(cacheRoot, ["explain", RuleId, workspace.SolutionPath, .. extra]);
    }

    // Cold, so the load count means "did this run reach MSBuild", with the persisted caches isolated at a
    // private root.
    private static Task<CliResult> RunAsync(string cacheRoot, params string[] args)
    {
        FakeEnvironment environment = FakeEnvironment.WithCacheRoot(cacheRoot);

        return CliRunner.InvokeColdAsync(environment, args);
    }
}
