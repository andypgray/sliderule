using Shouldly;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end tests for a partial check that loads only its dirty projects' closure rather than the whole
///     solution. MyApp's reference chain is <c>Domain → Web → Billing</c>, so a body edit in Web dirties Web
///     alone and its closure is Web and Billing: Domain is the project such a run never loads. Every row holds
///     the run to a <c>--no-cache</c> run of the same tree on every output channel.
/// </summary>
public sealed class CheckCacheClosureE2ETests
{
    private const string Billing = "MyApp.Legacy.Billing";
    private const string Domain = "MyApp.Domain";
    private const string Web = "MyApp.Web";

    [Fact]
    public async Task Check_MethodBodyEditInWeb_LoadsOnlyWebsClosureAndMatchesANoCacheRun()
    {
        using var workspace = new TempFixtureWorkspace();
        using var cache = new TempCacheRoot("check-closure");
        CacheRun cold = await RunCheckAsync(workspace.SolutionPath, cache.Root);
        cold.Outcome.ShouldBe(CodebaseSourceOutcome.Miss);
        EditWebBody(workspace);

        CacheRun partial = await RunCheckAsync(workspace.SolutionPath, cache.Root);
        CacheRun noCache = await RunCheckAsync(workspace.SolutionPath, cache.Root, true);

        // Web's closure was loaded, and nothing else: Domain, which references Web, was never opened.
        partial.Outcome.ShouldBe(CodebaseSourceOutcome.Partial);
        partial.ReExtracted.ShouldBe([Web]);
        partial.Source.ClosureRequests.Single()
            .ShouldBe([workspace.PathOf(Web, $"{Web}.csproj")]);
        partial.ShouldHaveLoadedOnly(true, Web, Billing);
        partial.Result.ShouldMatchTheOutputOf(noCache.Result);
        partial.Result.Out.ShouldNotBe(cold.Result.Out); // the edit's new red site was seen

        // And what the closure run wrote back validates whole: the next run is a hit with the same answer.
        CacheRun hit = await RunCheckAsync(workspace.SolutionPath, cache.Root);
        hit.Outcome.ShouldBe(CodebaseSourceOutcome.Hit);
        hit.Source.AcquireCount.ShouldBe(0);
        hit.Result.ShouldMatchTheOutputOf(noCache.Result);
    }

    [Fact]
    public async Task Graph_MethodBodyEditInWeb_LoadsOnlyWebsClosureAndMatchesANoCacheRun()
    {
        using var workspace = new TempFixtureWorkspace();
        using var cache = new TempCacheRoot("check-closure");
        await CacheRuns.GraphAsync(workspace.SolutionPath, cache.Root);
        EditWebBody(workspace);

        CacheRun partial = await CacheRuns.GraphAsync(workspace.SolutionPath, cache.Root);
        CacheRun noCache = await CacheRuns.GraphAsync(workspace.SolutionPath, cache.Root, true);

        partial.Outcome.ShouldBe(CodebaseSourceOutcome.Partial);
        partial.ShouldHaveLoadedOnly(true, Web, Billing);
        partial.Result.ShouldMatchTheOutputOf(noCache.Result);
    }

    /// <summary>
    ///     The partial twins of the store's recorded-verdict replay on a hit: a closure never loads Domain, so
    ///     what it reports about Domain is what the last whole load recorded, and that has to be what a whole
    ///     load of the edited tree reports too.
    /// </summary>
    [Theory]
    [InlineData("restore-failed")]
    [InlineData("failed")]
    public async Task Check_CleanProjectOutsideTheClosureRecordedAsBroken_StillReportedOnAClosurePartial(string verdict)
    {
        using var workspace = new TempFixtureWorkspace();
        using var cache = new TempCacheRoot("check-closure");
        ISolutionSource broken = BrokenDomain(workspace, verdict);
        await RunCheckAsync(workspace.SolutionPath, cache.Root, source: new CountingSolutionSource(broken));
        EditWebBody(workspace);

        CacheRun partial = await RunCheckAsync(workspace.SolutionPath, cache.Root);
        CacheRun noCache = await RunCheckAsync(
            workspace.SolutionPath, cache.Root, true, new CountingSolutionSource(BrokenDomain(workspace, verdict)));

        partial.Outcome.ShouldBe(CodebaseSourceOutcome.Partial);
        partial.ShouldHaveLoadedOnly(true, Web, Billing);
        (partial.Result.Out + partial.Result.Err).ShouldContain(Domain);
        partial.Result.Exit.ShouldBe(ExitCodes.Error);
        partial.Result.ShouldMatchTheOutputOf(noCache.Result);
    }

    [Fact]
    public async Task Check_ClosureThatFindsAFailureTheRecordDoesNotHold_LoadsTheWholeSolution()
    {
        using var workspace = new TempFixtureWorkspace();
        using var cache = new TempCacheRoot("check-closure");
        await RunCheckAsync(workspace.SolutionPath, cache.Root);
        EditWebBody(workspace);
        var closureFails = new CountingSolutionSource(new ClosureFailingSolutionSource());

        CacheRun partial = await RunCheckAsync(workspace.SolutionPath, cache.Root, source: closureFails);
        CacheRun noCache = await RunCheckAsync(workspace.SolutionPath, cache.Root, true);

        // The closure was asked for, judged unable to stand in for the solution, and replaced by a whole load.
        partial.Outcome.ShouldBe(CodebaseSourceOutcome.Partial);
        partial.Source.Loads.Select(load => load.IsClosure)
            .ShouldBe([true, false]);
        partial.Source.Loads[1].Projects.ShouldBe([Billing, Domain, Web], true);
        partial.Result.ShouldMatchTheOutputOf(noCache.Result);
    }

    [Fact]
    public async Task Check_PartialWhoseSpecWasNeverRecorded_LoadsTheWholeSolution()
    {
        // graph records no spec resolution, so a convention spec cannot be replayed on the partial that
        // follows, and resolving it needs the whole solution. MyApp has no spec project, so that resolution
        // refuses, the same way it refuses with no cache at all.
        using var workspace = new TempFixtureWorkspace();
        using var cache = new TempCacheRoot("check-closure");
        await CacheRuns.GraphAsync(workspace.SolutionPath, cache.Root);
        EditWebBody(workspace);
        var partialSource = new CountingSolutionSource();
        var noCacheSource = new CountingSolutionSource();

        var partial = await Should.ThrowAsync<UserErrorException>(() => RunCheckAsync(workspace.SolutionPath, cache.Root, source: partialSource, conventionSpec: true));
        var noCache = await Should.ThrowAsync<UserErrorException>(() => RunCheckAsync(workspace.SolutionPath, cache.Root, true, noCacheSource, true));

        partialSource.ClosureRequests.ShouldBeEmpty();
        partialSource.Loads.Single()
            .IsClosure.ShouldBeFalse();
        partialSource.Loads.Single()
            .Projects.ShouldBe([Billing, Domain, Web], true);
        partial.Message.ShouldBe(noCache.Message);
    }

    [Fact]
    public async Task Check_ThroughAFilter_BodyEditInBillingLoadsBillingAloneAndMatchesANoCacheRun()
    {
        // A Billing-only filter narrows the run; the closure load goes through a filter of its own over the
        // referenced solution, and the narrowing the run reports is the one the first run recorded.
        using var workspace = new TempFixtureWorkspace();
        using var cache = new TempCacheRoot("check-closure");
        string filter = workspace.PathOf("BillingOnly.slnf");
        await File.WriteAllTextAsync(
            filter,
            $$"""{ "solution": { "path": "MyApp.sln", "projects": [ "{{Billing}}/{{Billing}}.csproj" ] } }""",
            Ct);
        await RunCheckAsync(filter, cache.Root);
        FixtureEdits.EditOnDisk(
            workspace.PathOf(Billing, "BillingFacade.cs"),
            content => content.Replace(
                "        return calculator", "        _ = System.DateTime.Now;\n        return calculator", StringComparison.Ordinal));

        CacheRun partial = await RunCheckAsync(filter, cache.Root);
        CacheRun noCache = await RunCheckAsync(filter, cache.Root, true);

        partial.Outcome.ShouldBe(CodebaseSourceOutcome.Partial);
        partial.ReExtracted.ShouldBe([Billing]);
        partial.ShouldHaveLoadedOnly(true, Billing);
        partial.Result.ShouldMatchTheOutputOf(noCache.Result);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    // One more ambient-clock read inside an existing method body of Web: Web's own facts change, and no
    // declaration does.
    private static void EditWebBody(TempFixtureWorkspace workspace)
    {
        FixtureEdits.EditOnDisk(
            workspace.PathOf(Web, "HomeController.cs"),
            content => content.Replace(
                "        log.Append(description);", "        log.Append(System.DateTime.Now);\n        log.Append(description);"));
    }

    // A whole cold load that reports Domain as restore-failed, or a project outside the solution as failed to
    // load, beside a workspace message: a verdict only a whole load can have measured.
    private static ISolutionSource BrokenDomain(TempFixtureWorkspace workspace, string verdict)
    {
        return verdict == "restore-failed"
            ? new DiagnosticInjectingSolutionSource(
                [DiagnosticInjectingSolutionSource.SimulatedLoadFailure],
                restoreFailedProjects: [workspace.PathOf(Domain, $"{Domain}.csproj")],
                inner: new ColdSolutionSource())
            : new DiagnosticInjectingSolutionSource(
                [DiagnosticInjectingSolutionSource.SimulatedLoadFailure],
                [workspace.PathOf(Domain, $"{Domain}.csproj")],
                inner: new ColdSolutionSource());
    }

    private static Task<CacheRun> RunCheckAsync(
        string solution, string cacheRoot, bool noCache = false, CountingSolutionSource? source = null,
        bool conventionSpec = false)
    {
        string? spec = conventionSpec ? null : CliRunner.ViolatedSpecDll;

        return CacheRuns.CheckAsync(solution, spec, cacheRoot, noCache, source: source);
    }

    // A cold source whose closure loads report a project that failed to load, which no recorded verdict holds.
    private sealed class ClosureFailingSolutionSource : ISolutionSource
    {
        private readonly ColdSolutionSource cold = new();

        public Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
        {
            return cold.AcquireAsync(solutionPath, ct);
        }

        public async Task<SolutionHandle> AcquireClosureAsync(
            string solutionPath, IReadOnlyCollection<string> projectFiles, CancellationToken ct)
        {
            SolutionHandle real = await cold.AcquireClosureAsync(solutionPath, projectFiles, ct);
            WorkspaceDiagnostics failed = real.LoadDiagnostics with
            {
                FailedProjects = [DiagnosticInjectingSolutionSource.SimulatedFailedProject]
            };
            return new SolutionHandle(
                real.Solution, real.SolutionPath, failed, real, targetFrameworks: real.TargetFrameworks,
                isClosure: real.IsClosure);
        }
    }
}

file static class CheckCacheClosureAssertions
{
    // The run made one acquisition, of the kind given, and it loaded exactly these projects.
    internal static void ShouldHaveLoadedOnly(this CacheRun run, bool isClosure, params string[] projects)
    {
        (bool IsClosure, IReadOnlySet<string> Projects) load = run.Source.Loads.ShouldHaveSingleItem();
        load.IsClosure.ShouldBe(isClosure);
        load.Projects.ShouldBe(projects, true);
    }
}
