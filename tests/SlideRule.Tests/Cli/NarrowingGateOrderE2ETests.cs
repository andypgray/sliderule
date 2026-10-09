using Shouldly;
using Xunit;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Cli;

/// <summary>
///     What a run says when it is <em>both</em> broken and narrowed — a filter-selected project that failed
///     to load, which is one command away from ordinary — and what it says when the model is broken in both
///     of the ways it can be.
/// </summary>
/// <remarks>
///     <para>
///         A broken model outranks a small one. The two conditions are independent and can hold at
///         once, so each verb that refuses has to pick a first sentence. It picks the incomplete-model gate:
///         a narrowed universe is a smaller true answer and says so, while a partial model is a wrong one and
///         its refusal names the build that would fix it. <c>context</c>, which refuses nothing, writes both
///         blocks in that same order.
///     </para>
///     <para>
///         Within the broken model, the load failure outranks the failed restore. A project that
///         never loaded declares nothing at all; one whose packages did not resolve loaded completely and is
///         merely missing its package edges. Both blocks print rather than the first winning, because they name
///         different repairs — <c>dotnet build</c> and <c>dotnet restore</c> — and a reader who ran only the
///         one they were told about would still be stuck.
///     </para>
///     <para>
///         The narrowing has no opt-out. <c>--allow-workspace-diagnostics</c> takes the partial model and says
///         nothing about how much of the solution was checked, so with the flag the gate stands down and the
///         narrowing refusal is what the operator meets instead. Those rows prove the narrowing refusal is
///         second in line rather than shadowed by the gate, and the fix it names (run the solution the filter
///         references) is the only way past it.
///     </para>
///     <para>
///         Driven through the runners with a <see cref="DiagnosticInjectingSolutionSource" /> over a private
///         copy of the MyApp fixture, because no fixture is both: the fixture loads cleanly by design, and
///         injection is the only way to put a load failure and a narrowing in front of one command.
///         <see cref="FilteredSolutionE2ETests" /> owns the narrowing over a real <c>.slnf</c>, and
///         <see cref="PartialLoadWorkspaceE2ETests" /> owns the genuinely partial load.
///     </para>
/// </remarks>
public sealed class NarrowingGateOrderE2ETests
{
    private const string BrokenProject = DiagnosticInjectingSolutionSource.SimulatedFailedProject;

    private const string BaselineGateLede =
        "error: the model is incomplete — 1 project failed to load, so no baseline was written: a baseline "
        + "captured from a partial model signs off debt that was never measured:";

    private const string RenderGateLede =
        "error: the model is incomplete — 1 project failed to load, so nothing was rendered: a card whose "
        + "project failed to load cannot be placed and would be dropped from the committed files, and "
        + "--diagram would draw a survey missing whole projects:";

    private const string UnrestoredProject = "C:/repo/MyApp.Unrestored/MyApp.Unrestored.csproj";

    private const string RenderRestoreGateLede =
        "error: the model is incomplete — NuGet packages did not resolve for 1 project, so nothing was "
        + "rendered: a card would be committed describing dependencies the model never resolved, and "
        + "--diagram would draw a survey with the external edges missing:";

    // The one fragment that says the narrowing spoke, whichever block it spoke in.
    private const string AnyNarrowing = "narrowed this run";

    private static readonly string[] ConventionalBaselineFile =
        ["arch", "baselines", "data-access", "no-inline-sql.json"];

    [Fact]
    public async Task BaselineInit_BrokenAndNarrowed_RefusesOnTheGateAndNeverMentionsTheNarrowing()
    {
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile); // uncaptured, so a run that reached the write would create it

        CliResult init = await RunBaselineAsync(workspace, allowWorkspaceDiagnostics: false);

        init.ShouldRefuseWith(BaselineGateLede, BrokenProject);
        init.Err.ShouldNotContain(AnyNarrowing); // one refusal, and it is the one naming the build that fixes it
        File.Exists(baselineFile)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task BaselineInit_BrokenAndNarrowedWithTheOptOut_StillRefusesOnTheNarrowing()
    {
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile);

        CliResult init = await RunBaselineAsync(workspace, allowWorkspaceDiagnostics: true);

        init.ShouldRefuseWith(
            "narrowed this run — 1 project the solution declares was not checked, so no baseline was written",
            "MyApp.Skipped/MyApp.Skipped.csproj",
            "Run baseline against the solution the filter references rather than through the filter.");
        init.Err.ShouldNotContain("error: the model is incomplete"); // the gate stood down, as asked
        File.Exists(baselineFile)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Render_BrokenAndNarrowed_RefusesOnTheGateAndNeverMentionsTheNarrowing()
    {
        // Both refusals end "so nothing was rendered", so the lede is the whole discriminator here.
        using var workspace = new TempFixtureWorkspace();

        CliResult render = await RunRenderAsync(workspace, allowWorkspaceDiagnostics: false);

        render.ShouldRefuseWith(RenderGateLede, BrokenProject);
        render.Err.ShouldNotContain(AnyNarrowing);
        render.Out.ShouldBeEmpty();
    }

    [Fact]
    public async Task Render_BrokenAndNarrowedWithTheOptOut_StillRefusesOnTheNarrowing()
    {
        using var workspace = new TempFixtureWorkspace();

        CliResult render = await RunRenderAsync(workspace, allowWorkspaceDiagnostics: true);

        render.ShouldRefuseWith(
            "narrowed this run — 1 project the solution declares was not checked, so nothing was rendered",
            "Run render against the solution the filter references rather than through the filter.");
        render.Err.ShouldNotContain("error: the model is incomplete");
        render.Out.ShouldBeEmpty();
    }

    [Fact]
    public async Task Context_BrokenAndNarrowed_WritesTheCaveatFirstAndTheNarrowingStampBelowIt()
    {
        // Context refuses nothing, so it is the one verb that prints both — which makes it the only place
        // the order is visible rather than inferred from which message won.
        using var workspace = new TempFixtureWorkspace();
        var output = new StringWriter();
        string solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(workspace.SolutionPath))!;
        var source = new DiagnosticInjectingSolutionSource([], [BrokenProject], [SkippedProject(workspace)]);

        int exit = await new ContextRunner(output, source).RunAsync(
            new ContextRequest(
                "MyApp.Legacy.Billing", workspace.SolutionPath, CliRunner.CleanSpecDll, solutionDirectory),
            Ct);

        exit.ShouldBe(0); // a lookup, whatever else is true of the run
        var answer = output.ToString();
        answer.ShouldStartWith("caveat: the model is incomplete");
        answer.ShouldContain("'MyApp.sln' narrowed this run: 1 project the solution declares was not checked.");
        answer.ShouldContain("  MyApp.Skipped/MyApp.Skipped.csproj");
        answer.IndexOf(AnyNarrowing, StringComparison.Ordinal)
            .ShouldBeGreaterThan(answer.IndexOf(BrokenProject, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Render_BrokenAndUnrestoredAndNarrowed_WritesTheLoadBlockThenTheRestoreBlockAndStopsThere()
    {
        using var workspace = new TempFixtureWorkspace();
        var source = new DiagnosticInjectingSolutionSource(
            [], [BrokenProject], [SkippedProject(workspace)], [UnrestoredProject]);
        var request = new RenderRequest(
            workspace.SolutionPath, CliRunner.CleanSpecDll,
            SolutionPaths.SolutionDirectoryOf(workspace.SolutionPath), true, false);

        CliResult render = await CliResult.CapturedAsync((output, error) => new RenderRunner(output, error, source).RunAsync(request, Ct));

        render.ShouldRefuseWith(RenderGateLede, BrokenProject, RenderRestoreGateLede, UnrestoredProject);
        render.Err.IndexOf(RenderRestoreGateLede, StringComparison.Ordinal)
            .ShouldBeGreaterThan(render.Err.IndexOf(RenderGateLede, StringComparison.Ordinal));
        render.Err.ShouldNotContain(AnyNarrowing);
        render.Out.ShouldBeEmpty();
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    // Every request below is cache-free. The subject here is the order two gates speak in over an injected
    // set of load diagnostics, and a cache hit replays a real run's diagnostics instead of the injected ones.

    // Injected under the solution directory so the notice relativizes it to a path this class can pin; an
    // unchecked project spelled anywhere else would render as a machine-specific climb out of a temp root.
    private static string SkippedProject(TempFixtureWorkspace workspace)
    {
        string solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(workspace.SolutionPath))!;
        return Path.Combine(solutionDirectory, "MyApp.Skipped", "MyApp.Skipped.csproj");
    }

    private static Task<CliResult> RunBaselineAsync(
        TempFixtureWorkspace workspace, bool allowWorkspaceDiagnostics)
    {
        var source = new DiagnosticInjectingSolutionSource([], [BrokenProject], [SkippedProject(workspace)]);
        BaselineRequest request = BaselineRequests.Init(SolutionPaths.SolutionDirectoryOf(workspace.SolutionPath))
            with
            {
                Solution = workspace.SolutionPath,
                Spec = CliRunner.ViolatedSpecDll,
                NoCache = true,
                AllowWorkspaceDiagnostics = allowWorkspaceDiagnostics
            };

        return CliResult.CapturedAsync((output, error) => new BaselineRunner(output, error, source).RunAsync(request, Ct));
    }

    private static Task<CliResult> RunRenderAsync(
        TempFixtureWorkspace workspace, bool allowWorkspaceDiagnostics)
    {
        var source = new DiagnosticInjectingSolutionSource([], [BrokenProject], [SkippedProject(workspace)]);
        var request = new RenderRequest(
            workspace.SolutionPath, CliRunner.CleanSpecDll,
            SolutionPaths.SolutionDirectoryOf(workspace.SolutionPath), true, allowWorkspaceDiagnostics);

        return CliResult.CapturedAsync((output, error) => new RenderRunner(output, error, source).RunAsync(request, Ct));
    }
}
