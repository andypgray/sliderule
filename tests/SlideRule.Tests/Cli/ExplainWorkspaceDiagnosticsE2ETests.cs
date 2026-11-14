using Shouldly;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>The workspace-diagnostics contract on <c>explain</c> — the verb that renders but never gates.</summary>
/// <remarks>
///     <para>
///         <c>explain</c> renders the composed diagnostics of the workspace it opens for resolution and still
///         answers, because the rule it dumps comes from the spec and not from the codebase — a project that
///         failed to load cannot make the answer wrong, only leave the reader unaware that anything failed.
///     </para>
///     <para>
///         Driven through <see cref="ExplainRunner" /> with a
///         <see cref="DiagnosticInjectingSolutionSource" /> over this repository's own solution and its
///         arch-spec csproj: the workspace path needs a solution whose spec resolves <em>through</em> that
///         workspace. MyApp declares no spec project, so the convention and csproj branches refuse there
///         before a workspace is ever consulted, and a built-DLL <c>--spec</c> takes the fast path instead. A
///         replayed cache record renders the failures it recorded, for a built DLL as for the convention, which
///         the LayoutApp fixture's spec project lets one test pair. A load that is itself partial is tested in
///         <see cref="PartialLoadWorkspaceE2ETests" />; what this class owns is which channel the failures reach.
///     </para>
/// </remarks>
public sealed class ExplainWorkspaceDiagnosticsE2ETests
{
    private const string LoadDiagnostic = DiagnosticInjectingSolutionSource.SimulatedLoadFailure;

    [Fact]
    public async Task Explain_WorkspacePathWithLoadFailure_RendersTheDiagnosticsAndStillAnswers()
    {
        // Cache-free: the subject is what a workspace load reports, and a hit answers with no workspace at all.
        CliResult result = await RunAsync(
            new ExplainRequest("cli/no-stdout", RepoRoot.Solution, RepoRoot.ArchitectureSpecCsproj, RepoRoot.Directory, true));

        result.ShouldSucceed("cli/no-stdout"); // the answer still arrives — it was never at risk
        result.Err.ShouldContain($"warning: {LoadDiagnostic}"); // and the load failure is visible beside it
        result.Err.ShouldContain("MSBuild for this run:"); // composed like every other verb's
        result.Err.ShouldNotContain("error: the model is incomplete"); // nothing gates: the answer is spec-derived
    }

    [Fact]
    public async Task Explain_DllFastPath_OpensNoWorkspaceSoThereIsNothingToRender()
    {
        // Silent by construction rather than by a condition: the fast path returns before the render, so this
        // source — which would have injected a load failure — is never acquired at all.
        CliResult result = await RunAsync(
            new ExplainRequest(
                "layering/domain-independent", CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll,
                SolutionPaths.SolutionDirectoryOf(CliRunner.MyAppSolution), true));

        result.ShouldSucceed("layering/domain-independent (enforce)");
        result.Err.ShouldBeEmpty();
    }

    [Fact]
    public async Task Explain_RecordReplayed_RendersTheRecordedDiagnosticsForTheBuiltDllAsForTheConvention()
    {
        // Two checks through a source that injects a load failure record it beside each spec form's resolution.
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        using var cache = new TempCacheRoot("explain-diagnostics");
        string dll = LayoutAppFixture.BuiltSpecDll(workspace);
        var injecting = new CountingSolutionSource(new DiagnosticInjectingSolutionSource([LoadDiagnostic]));
        await ShouldCheckCleanAsync(workspace, null, cache.Root, injecting);
        await ShouldCheckCleanAsync(workspace, dll, cache.Root, injecting);

        CacheRun convention = await CacheRuns.ExplainAsync(workspace.SolutionPath, LayoutAppFixture.RuleId, null, cache.Root);
        CacheRun built = await CacheRuns.ExplainAsync(workspace.SolutionPath, LayoutAppFixture.RuleId, dll, cache.Root);

        convention.Result.Err.ShouldContain($"warning: {LoadDiagnostic}");
        built.Result.Err.ShouldBe(convention.Result.Err);
        (convention.AcquireCount, built.AcquireCount).ShouldBe((0, 0));
    }

    private static async Task ShouldCheckCleanAsync(
        TempFixtureWorkspace workspace, string? spec, string cacheRoot, CountingSolutionSource source)
    {
        CacheRun check = await CacheRuns.CheckAsync(workspace.SolutionPath, spec, cacheRoot, source: source);
        check.Result.Exit.ShouldBe(0, check.Result.Transcript());
    }

    private static Task<CliResult> RunAsync(ExplainRequest request)
    {
        var source = new DiagnosticInjectingSolutionSource([LoadDiagnostic]);

        return CliResult.CapturedAsync((output, error) => new ExplainRunner(output, error, source, new SystemEnvironment()).RunAsync(request, Ct));
    }
}
