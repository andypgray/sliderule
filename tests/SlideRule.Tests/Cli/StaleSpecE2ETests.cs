using System.Text.Json;
using Shouldly;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     A spec source edited after its project's last build, run through the real CLI over the LayoutApp fixture,
///     whose solution declares a spec project: the warning on stderr and in a clean hook run's output, the sentence
///     above <c>explain</c>'s answer, the <c>staleSpec</c> stamp in the documents, on a cold run and on a cache hit,
///     the same for a spec passed as its built DLL, and the silence of the DLL form where no run has recorded which
///     project builds it.
/// </summary>
/// <remarks>
///     The edit adds a member to the spec class and changes no rule, so the verdict stays green and the warning is
///     the only difference a stale build makes to the output.
/// </remarks>
public sealed class StaleSpecE2ETests
{
    private const string SpecSource = "LayoutApp.Spec/LayoutAppSpec.cs";

    private const string Warning = "warning: " + LayoutAppFixture.StaleSpecSentence;

    private static readonly Dictionary<string, string> ExpectedStamp = new()
    {
        ["project"] = "LayoutApp.Spec/LayoutApp.Spec.csproj",
        ["file"] = SpecSource,
        ["reason"] = "changed",
        ["comparison"] = "checksum"
    };

    [Fact]
    public async Task Check_SpecSourceEditedAfterTheBuild_WarnsOnStderr()
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("stale-spec");
        using ScopedFile edit = LayoutAppFixture.EditSpecAfterBuild(workspace);

        CliResult result = await RunAsync(cache.Root, "check", workspace.SolutionPath);

        result.ShouldSucceed($"pass {LayoutAppFixture.RuleId}");
        result.Err.ShouldContain(Warning);
    }

    [Theory]
    [InlineData(false)] // the hit resolves the spec from the recorded project path, with no workspace
    // The spec project's own built DLL is that member's spec, so its sources are compared as the csproj form
    // compares them; the hit gets the project from the DLL's recorded resolution.
    [InlineData(true)]
    public async Task CheckJson_SpecSourceEditedAfterTheBuild_StampsTheDocumentOnAColdRunAndOnACacheHit(
        bool specAsItsBuiltDll)
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("stale-spec");
        using ScopedFile edit = LayoutAppFixture.EditSpecAfterBuild(workspace);
        string[] spec = specAsItsBuiltDll ? ["--spec", LayoutAppFixture.BuiltSpecDll(workspace)] : [];
        string[] check = ["check", workspace.SolutionPath, .. spec, "--json"];

        CliResult cold = await RunColdAsync(cache.Root, check);
        CliResult hit = await RunColdAsync(cache.Root, check);

        cold.Loads.ShouldBe(1);
        ShouldHaveStaleSpecStamp(cold).ShouldBe(ExpectedStamp);
        hit.Loads.ShouldBe(0);
        ShouldHaveStaleSpecStamp(hit).ShouldBe(ExpectedStamp);
    }

    [Fact]
    public async Task StatusJson_SpecSourceEditedAfterTheBuild_StampsTheDocument()
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("stale-spec");
        using ScopedFile edit = LayoutAppFixture.EditSpecAfterBuild(workspace);

        CliResult result = await RunAsync(cache.Root, "status", workspace.SolutionPath, "--json");

        ShouldHaveStaleSpecStamp(result).ShouldBe(ExpectedStamp);
    }

    [Theory]
    [InlineData(LayoutAppFixture.RuleId)]
    [InlineData("--type", "LayoutApp.Core.Ledger")]
    public async Task Explain_SpecSourceEditedAfterTheBuild_PutsTheSentenceAboveTheAnswerAndWarnsOnStderr(
        params string[] query)
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("stale-spec");
        CliResult fresh = await RunAsync(cache.Root, ["explain", .. query, workspace.SolutionPath]);
        fresh.ShouldSucceed();

        using ScopedFile edit = LayoutAppFixture.EditSpecAfterBuild(workspace);
        CliResult stale = await RunAsync(cache.Root, ["explain", .. query, workspace.SolutionPath]);

        stale.Out.ShouldBe(LayoutAppFixture.StaleSpecSentence + Environment.NewLine + Environment.NewLine + fresh.Out);
        stale.Err.ShouldContain(Warning);
    }

    [Fact]
    public async Task Explain_UnknownRuleOverAStaleSpec_RefusesWithNoSentenceAbove()
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("stale-spec");
        using ScopedFile edit = LayoutAppFixture.EditSpecAfterBuild(workspace);

        CliResult result = await RunAsync(cache.Root, "explain", "layout/no-such-rule", workspace.SolutionPath);

        result.Exit.ShouldNotBe(0, customMessage: result.Err);
        result.Out.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)] // the recording check sees the edit, so the explain's cache read is a hit
    [InlineData(false)] // the edit lands after the recording check, so the explain's read is partial
    public async Task Explain_SpecPassedAsItsBuiltDll_PutsTheSentenceAboveTheAnswerOnceACheckHasRecordedIt(
        bool editBeforeTheRecordingCheck)
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("stale-spec");
        string dll = LayoutAppFixture.BuiltSpecDll(workspace);
        CacheRun fresh = await ExplainTheDllAsync(cache.Root, workspace, dll);
        fresh.Result.ShouldSucceed();

        using ScopedFile? earlyEdit = editBeforeTheRecordingCheck ? LayoutAppFixture.EditSpecAfterBuild(workspace) : null;
        await ShouldRecordTheDllWithACheckAsync(cache.Root, workspace, dll);
        using ScopedFile? lateEdit = editBeforeTheRecordingCheck ? null : LayoutAppFixture.EditSpecAfterBuild(workspace);
        CacheRun stale = await ExplainTheDllAsync(cache.Root, workspace, dll);

        stale.Result.Out.ShouldBe(
            LayoutAppFixture.StaleSpecSentence + Environment.NewLine + Environment.NewLine + fresh.Result.Out);
        stale.Result.Err.ShouldBe(Warning + Environment.NewLine);
        (stale.Outcome, stale.AcquireCount).ShouldBe((CodebaseSourceOutcome.Hit, 0));
    }

    [Theory]
    [InlineData(false)] // no run has recorded which project builds the DLL
    [InlineData(true)] // one has, and --no-cache leaves the record unread
    public async Task Explain_SpecPassedAsItsBuiltDll_WithNoRecordRead_AnswersWithoutTheSentenceAndLoadsNothing(
        bool recordedAndBypassed)
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("stale-spec");
        using ScopedFile edit = LayoutAppFixture.EditSpecAfterBuild(workspace);
        string dll = LayoutAppFixture.BuiltSpecDll(workspace);
        if (recordedAndBypassed) await ShouldRecordTheDllWithACheckAsync(cache.Root, workspace, dll);

        CacheRun run = await ExplainTheDllAsync(cache.Root, workspace, dll, noCache: recordedAndBypassed);

        run.Result.ShouldSucceed();
        run.Result.Out.ShouldNotContain(LayoutAppFixture.StaleSpecSentence);
        run.Result.Err.ShouldBeEmpty();
        CodebaseSourceOutcome expected = recordedAndBypassed ? CodebaseSourceOutcome.Disabled : CodebaseSourceOutcome.Miss;
        (run.Outcome, run.AcquireCount).ShouldBe((expected, 0));
    }

    [Fact]
    public async Task BaselineRename_SpecPassedAsItsBuiltDll_WarnsOnStderrOnceACheckHasRecordedIt()
    {
        // The rename refuses on its own file work, after the spec has loaded, so the warning is all it writes first.
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("stale-spec");
        using ScopedFile edit = LayoutAppFixture.EditSpecAfterBuild(workspace);
        string dll = LayoutAppFixture.BuiltSpecDll(workspace);
        await ShouldRecordTheDllWithACheckAsync(cache.Root, workspace, dll);

        CliResult result = await RunColdAsync(
            cache.Root, "baseline", "rename", workspace.SolutionPath, "--spec", dll,
            "--from", "layout/old-name", "--to", LayoutAppFixture.RuleId);

        result.ShouldRefuseWithExactly(
            Warning + Environment.NewLine
                    + $"rule '{LayoutAppFixture.RuleId}' is not ratcheted — only Migrate and Quarantine containment rules carry baselines.");
        result.Loads.ShouldBe(0);
    }

    [Fact]
    public async Task CheckForTheHook_CleanRunOverAStaleSpec_WritesTheWarningForTheAgent()
    {
        // A clean hook run writes nothing unless it has a warning to hand on; a stale spec is one, because the
        // agent can rebuild and a green verdict on rules the source may have changed would otherwise pass unread.
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using ScopedFile edit = LayoutAppFixture.EditSpecAfterBuild(workspace);

        CliResult result = await CheckForTheHookAsync(workspace);

        result.Exit.ShouldBe(0, customMessage: result.Out);
        result.Out.ShouldContain(Warning);
    }

    [Fact]
    public async Task CheckForTheHook_CleanRunOverAFreshSpec_WritesNothing()
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();

        CliResult result = await CheckForTheHookAsync(workspace);

        result.Exit.ShouldBe(0, customMessage: result.Out);
        result.Out.ShouldBeEmpty();
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    // The document's staleSpec stamp as name-value pairs; a document without one fails, naming what it held.
    private static Dictionary<string, string> ShouldHaveStaleSpecStamp(CliResult result)
    {
        JsonElement root = result.ShouldHaveJsonStdout()
            .RootElement;
        root.TryGetProperty("staleSpec", out JsonElement stamp)
            .ShouldBeTrue($"the document carries no staleSpec stamp:{Environment.NewLine}{root.GetRawText()}"
                          + $"{Environment.NewLine}stderr:{Environment.NewLine}{result.Err}");
        return stamp.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!);
    }

    // A green check with the DLL as its spec, which records the DLL's resolution in the cache for a later run.
    private static async Task ShouldRecordTheDllWithACheckAsync(string cacheRoot, TempFixtureWorkspace workspace, string dll)
    {
        CliResult recording = await RunAsync(cacheRoot, "check", workspace.SolutionPath, "--spec", dll);
        recording.ShouldSucceed($"pass {LayoutAppFixture.RuleId}");
    }

    // The rule form of explain in process over a cold source, with the spec passed as its built DLL, so the run's
    // cache outcome and acquisitions can be read beside its output.
    private static Task<CacheRun> ExplainTheDllAsync(
        string cacheRoot, TempFixtureWorkspace workspace, string dll, bool noCache = false)
    {
        return CacheRuns.ExplainAsync(workspace.SolutionPath, LayoutAppFixture.RuleId, dll, cacheRoot, noCache);
    }

    // The check the hook verb runs, in process, over the convention spec.
    private static Task<CliResult> CheckForTheHookAsync(TempFixtureWorkspace workspace)
    {
        string solutionDirectory = Path.GetDirectoryName(workspace.SolutionPath)!;
        CheckRequest request = CheckRequests.For(workspace.SolutionPath, null, solutionDirectory);

        return CliResult.CapturedAsync((output, error) =>
        {
            var runner = new CheckRunner(output, error, WarmWorkspacePool.Source, new FakeEnvironment());
            return runner.RunForHookAsync(request, Ct);
        });
    }

    // Over the warm workspace pool, with the persisted caches isolated at a private root.
    private static Task<CliResult> RunAsync(string cacheRoot, params string[] args)
    {
        FakeEnvironment environment = FakeEnvironment.WithCacheRoot(cacheRoot);

        return CliRunner.InvokeAsync(environment, args);
    }

    // Cold, so the load count means "did this run reach MSBuild", with the persisted caches isolated at a
    // private root.
    private static Task<CliResult> RunColdAsync(string cacheRoot, params string[] args)
    {
        FakeEnvironment environment = FakeEnvironment.WithCacheRoot(cacheRoot);

        return CliRunner.InvokeColdAsync(environment, args);
    }
}
