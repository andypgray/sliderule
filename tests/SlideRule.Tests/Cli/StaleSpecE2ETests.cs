using System.Text.Json;
using Shouldly;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     A spec source edited after its project's last build, run through the real CLI over the LayoutApp fixture,
///     whose solution declares a spec project: the warning on stderr and in a clean hook run's output, the sentence
///     above <c>explain</c>'s answer, the <c>staleSpec</c> stamp in the documents, on a cold run and on a cache hit,
///     and the silence of a spec passed as its DLL.
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

    [Fact]
    public async Task CheckJson_SpecSourceEditedAfterTheBuild_StampsTheDocumentOnAColdRunAndOnACacheHit()
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("stale-spec");
        using ScopedFile edit = LayoutAppFixture.EditSpecAfterBuild(workspace);

        CliResult cold = await RunColdAsync(cache.Root, "check", workspace.SolutionPath, "--json");
        CliResult hit = await RunColdAsync(cache.Root, "check", workspace.SolutionPath, "--json");

        cold.Loads.ShouldBe(1);
        ShouldHaveStaleSpecStamp(cold).ShouldBe(ExpectedStamp);
        hit.Loads.ShouldBe(0); // the hit resolves the spec from the recorded project path, with no workspace
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

    [Fact]
    public async Task Check_SpecPassedAsItsDll_DoesNotCompareSources()
    {
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("stale-spec");
        using ScopedFile edit = LayoutAppFixture.EditSpecAfterBuild(workspace);
        string dll = LayoutAppFixture.BuiltSpecDll(workspace);

        CliResult result = await RunAsync(cache.Root, "check", workspace.SolutionPath, "--spec", dll, "--json");

        result.ShouldHaveJsonStdout()
            .RootElement.TryGetProperty("staleSpec", out _)
            .ShouldBeFalse("a spec passed as a DLL names no project, so there are no sources to compare");
        result.Err.ShouldNotContain("differs from its last build");
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
