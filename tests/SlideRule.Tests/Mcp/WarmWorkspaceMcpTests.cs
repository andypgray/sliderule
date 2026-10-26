using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Shouldly;
using SlideRule.Cli.Mcp;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.SpecLoading;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Hosting;
using SlideRule.Roslyn.Sessions;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     With the warm <see cref="WorkspaceSession" /> wired behind the MCP
///     tools, a server answers many tool calls against one loaded solution, reconciled against disk at each
///     call. These tests drive the real <see cref="McpPipelineHarness" /> (the production DI graph, warm by
///     default) and assert on the deterministic observables — the session's <c>SweepContentReads</c> /
///     <c>FullReloadCount</c> counters reached through the harness <see cref="McpPipelineHarness.Services" />
///     accessor, byte-equality against a cold CLI run, and cold/warm source selection — never on wall
///     time.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="CliMcpParityTests" /> is the broad warm-path parity net; this suite pins the
///         warm-specific behaviour that parity cannot see.
///     </para>
///     <para>
///         Every CLI leg here goes through <see cref="CliRunner.InvokeColdAsync(string[])" />, never the
///         warm-by-default <see cref="CliRunner.InvokeAsync(string[])" />, so the reference side never reads the
///         warm session the harness holds. The oracle in each case is a cold run over the edited tree:
///         warm-equals-cold is the claim, so the reference side has to be cold or the comparison proves nothing.
///         That cold side is fronted by the run's persisted cache, so a leg without <c>--no-cache</c> may answer
///         as a hit or a closure partial over fragments an earlier cold leg wrote for the same path, a path the
///         golden suite holds to a fresh load.
///     </para>
///     <para>
///         The suite uses two fixtures, and they are not interchangeable. Most cases drive the MyApp copy
///         with a prebuilt spec DLL, which is the cheap way to vary the checked universe. The two
///         spec-resolution cases cannot: a <c>--spec &lt;dll&gt;</c> resolves before the workspace is touched,
///         so it never reaches the walk they are about. They take
///         <see cref="LayoutAppFixture">the output-layout fixture</see> instead — the only fixture solution
///         that declares a spec project — and pay a real restore and build for it, once per class thanks to
///         the lease.
///     </para>
/// </remarks>
public sealed class WarmWorkspaceMcpTests
{
    private const string Domain = "MyApp.Domain";
    private const string Web = "MyApp.Web";
    private const string Billing = "MyApp.Legacy.Billing";

    // The Web layer's broad catcher, and the rule that reds it — the two values the pair of catch cases
    // below name over and over, which is what earns them a const each where a rule id used twice stays a
    // literal at its call site.
    private const string ReportEndpoint = "MyApp.Web.ReportEndpoint";
    private const string CatchRule = "exceptions/no-general-catch";

    private const string SaveMemberId = "M:MyApp.Web.HomeController.Save";
    private const string LoadMemberId = "M:MyApp.Web.HomeController.Load";
    private const string DeleteMemberId = "M:MyApp.Web.HomeController.Delete";
    private const string SaveAsyncMemberId = "M:MyApp.Web.HomeController.SaveAsync";
    private const string NowMemberId = "P:System.DateTime.Now";

    // A one-rule spec (member-subject MustAcceptParameter): Web Task-returning methods must accept a
    // CancellationToken. Compiled to a throwaway DLL by SpecAssemblyCompiler because no committed fixture
    // spec carries this rule in isolation (MyAppViolatedSpec has it, among many unrelated rules).
    // String-selected subject + BCL anchors only, so the spec depends on nothing but the core and the BCL.
    private const string AcceptCancellationSpecSource =
        """
        using System.Threading;
        using System.Threading.Tasks;
        using SlideRule;

        namespace WarmAcceptCancellation
        {
            public sealed class AcceptCancellationSpec : IArchitectureSpec
            {
                public void Define(Arch arch)
                {
                    arch.Rule("async/accept-cancellation")
                        .Enforce(arch.Namespace("MyApp.Web.*").Methods.Returning(typeof(Task), typeof(Task<>))
                            .MustAcceptParameter(typeof(CancellationToken)))
                        .Because("Async Web methods must honor cancellation.");
                }
            }
        }
        """;

    [Fact]
    public async Task ArchCheck_SourceFileEditedOnDisk_ReflectsEditAndMatchesColdCli()
    {
        // Arrange — a warm server bound to a private fixture copy + the quarantined spec, whose containment turns
        // a NEW inbound reference into the quarantined scope hard-red, so an on-disk edit visibly changes the
        // check report.
        // Act — add a new inbound reference (HomeController -> BillingCalculator) on disk, then re-check the
        // still-warm server. The inserted member is the QuarantineContainmentE2ETests edit, so the resulting red
        // edge is the one that suite pins.
        WarmRecheck recheck = await ShouldRecheckAfterEditAsync(
            CliRunner.QuarantinedSpecDll,
            "HomeController.cs",
            path => FixtureEdits.AppendMemberLine(
                path, "    public BillingCalculator NewCalculator() => new BillingCalculator();"));

        // Assert — the warm re-check reflects the edit (the new red edge appears, and the payload changed)
        // and is byte-identical to the cold run on the edited tree.
        recheck.After.ShouldContain("MyApp.Legacy.Billing.BillingCalculator");
        recheck.ShouldHaveMovedToTheColdAnswer();

        // …and the incremental store re-walked exactly the edited project (HomeController is in Web) plus its
        // reverse-dependent Domain — Billing was reused, not re-extracted.
        recheck.ReExtracted.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_MemberBanReadAddedOnDisk_ReflectsNewSiteAndMatchesColdCli()
    {
        // Arrange — a warm server bound to the violated spec, whose member-level Migrate rule time/inject-clock
        // (uncaptured) is already red on HomeController's two ambient-clock reads (GRAMMAR §4.5).
        // Act — append a THIRD banned read (another DateTime.Now use) on disk, then re-check the still-warm
        // server. The new read folds into the existing Now violation's site set rather than minting a new
        // violation identity.
        WarmRecheck recheck = await ShouldRecheckAfterEditAsync(
            CliRunner.ViolatedSpecDll,
            "HomeController.cs",
            path => FixtureEdits.AppendMemberLine(
                path, "    public System.DateTime ExportStampAgain() => System.DateTime.Now;"));

        // Assert — the warm re-check reflects the new member-use site (the Now violation's site set grows from one
        // to two), the payload changed, and it is byte-identical to the cold run on the edited tree.
        recheck.Before.ShouldHaveViolationAtSites("time/inject-clock", ("targetMember", NowMemberId), 1);
        recheck.After.ShouldHaveViolationAtSites("time/inject-clock", ("targetMember", NowMemberId), 2);
        recheck.ShouldHaveMovedToTheColdAnswer();

        // …and the incremental store re-walked exactly the edited project (Web) plus its reverse-dependent Domain.
        recheck.ReExtracted.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_UnsuffixedTaskMethodAddedOnDisk_ReflectsNewMemberShapeRedAndMatchesColdCli()
    {
        // Arrange — a warm server bound to the violated spec, whose member-subject Migrate rule
        // naming/async-suffix (uncaptured) is already red on HomeController's two unsuffixed Task-returning
        // methods Save/Load (GRAMMAR §4.6). This is the member-SUBJECT analog of the member-USE test above.
        // Act — append a THIRD unsuffixed Task-returning method (Delete) on disk, then re-check the still-warm
        // server. The new method mints its own member-subject violation identity (M:...Delete) under
        // naming/async-suffix (GRAMMAR §4.6).
        WarmRecheck recheck = await ShouldRecheckAfterEditAsync(
            CliRunner.ViolatedSpecDll,
            "HomeController.cs",
            path => FixtureEdits.AppendMemberLine(
                path,
                "    public System.Threading.Tasks.Task Delete() => System.Threading.Tasks.Task.CompletedTask;"));

        // Assert — the warm re-check reflects the new member-shape red (the async-suffix subject set grows from
        // {Save, Load} to {Save, Load, Delete}, the new one keying its own M: DocId), the payload changed, and
        // it is byte-identical to the cold run on the edited tree.
        recheck.Before.ShouldHaveFailedWith("naming/async-suffix", "subjectMember", [SaveMemberId, LoadMemberId]);
        recheck.After.ShouldHaveFailedWith(
            "naming/async-suffix", "subjectMember", [SaveMemberId, LoadMemberId, DeleteMemberId]);
        recheck.ShouldHaveMovedToTheColdAnswer();

        // …and the incremental store re-walked exactly the edited project (Web) plus its reverse-dependent Domain.
        recheck.ReExtracted.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_CancellationTokenParamAddedOnDisk_ClearsMemberShapeRedAndMatchesColdCli()
    {
        // Arrange — compile a one-rule spec DLL (member-subject MustAcceptParameter) to a private temp dir,
        // then bind a warm server to a fixture copy + that spec. HomeController's three tokenless Task-returning
        // methods Save/Load/SaveAsync are all red (GRAMMAR §4.6). This is the parameter-facts warm analog of the
        // async-suffix member-subject test above — it proves Parameters survive the warm reconcile + cache path.
        string specDll = SpecAssemblyCompiler.EmitSpecDllToFreshTemp(
            "warm-mcp", "WarmAcceptCancellation", AcceptCancellationSpecSource);

        // Act — add a CancellationToken parameter to the tokenless Save on disk, then re-check the
        // still-warm server.
        WarmRecheck recheck = await ShouldRecheckAfterEditAsync(
            specDll,
            "HomeController.cs",
            path => FixtureEdits.EditOnDisk(path, AddCancellationTokenToSave));

        // Assert — Save's member-shape red clears (Save now accepts the token, so the accept-cancellation
        // subject set shrinks from {Save, Load, SaveAsync} to {Load, SaveAsync}), the payload changed, and
        // the warm result is byte-identical to the cold run on the edited tree.
        recheck.Before.ShouldHaveFailedWith(
            "async/accept-cancellation", "subjectMember", [SaveMemberId, LoadMemberId, SaveAsyncMemberId]);
        recheck.After.ShouldHaveFailedWith(
            "async/accept-cancellation", "subjectMember", [LoadMemberId, SaveAsyncMemberId]);
        recheck.ShouldHaveMovedToTheColdAnswer();

        // …and the incremental store re-walked exactly the edited project (Web) plus its reverse-dependent Domain.
        recheck.ReExtracted.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_RegistrationLifetimeFlippedOnDisk_ReflectsChangedCaptiveSetAndMatchesColdCli()
    {
        // Arrange — a warm server bound to the violated spec, whose injection Migrate rule
        // di/no-captive-dependencies (uncaptured) is already red on the singleton ReportScheduler's two captive
        // edges: a scoped IOrderFeed and a transient IOrderFormatter (GRAMMAR §4.7). This is the DI-axis analog
        // of the member edit tests above — an on-disk REGISTRATION change (not a reference/member edit).
        // Act — flip IOrderFeed's registration from scoped to singleton on disk, then re-check the still-warm
        // server. A singleton injecting a now-singleton dependency is not captive, so the IOrderFeed edge drops
        // out of the violation set.
        WarmRecheck recheck = await ShouldRecheckAfterEditAsync(
            CliRunner.ViolatedSpecDll,
            "ServiceWiring.cs",
            path => FixtureEdits.EditOnDisk(path, FlipOrderFeedToSingleton));

        // Assert — the warm re-check reflects the flipped lifetime (the captive set shrinks from
        // {IOrderFeed, IOrderFormatter} to just {IOrderFormatter}), the payload changed, and it is
        // byte-identical to the cold run on the edited tree — the registration pass re-ran for Web.
        recheck.Before.ShouldHaveFailedWith(
            "di/no-captive-dependencies", "target", ["MyApp.Web.IOrderFeed", "MyApp.Web.IOrderFormatter"]);
        recheck.After.ShouldHaveFailedWith("di/no-captive-dependencies", "target", ["MyApp.Web.IOrderFormatter"]);
        recheck.ShouldHaveMovedToTheColdAnswer();

        // …and the incremental store re-walked exactly the edited project (ServiceWiring is in Web) and nothing
        // else: the registration call is inside a method body, so Domain's view of Web did not move.
        recheck.ReExtracted.ShouldBe([Web]);
    }

    [Fact]
    public async Task ArchCheck_SecondSwallowingCatchAddedOnDisk_ReflectsNewCatchSiteAndMatchesColdCli()
    {
        // Arrange — a warm server bound to the violated spec, whose catch Migrate rule exceptions/no-general-catch
        // (uncaptured) is already red on ReportEndpoint's blanket catch (GRAMMAR §4.8). This is the catch-axis
        // analog of the member-use edit test: adding a SECOND catch (Exception) to the SAME type is the SAME
        // (source, caught) identity, so the one catch violation just grows a second site — not a new identity.
        // Act — append a second swallowing catch (System.Exception) on the SAME Web type on disk, then re-check
        // the still-warm server. The second site folds into the existing (ReportEndpoint, System.Exception)
        // violation (GRAMMAR §4.8).
        WarmRecheck recheck = await ShouldRecheckAfterEditAsync(
            CliRunner.ViolatedSpecDll,
            "ReportEndpoint.cs",
            path => FixtureEdits.AppendMemberLine(
                path,
                "    public int RenderAgain(int id) { try { return id; } catch (System.Exception) { return -1; } }"));

        // Assert — the violation identity is unchanged (still ONE catch violation for the (ReportEndpoint,
        // Exception) pair), its site set grows from one to two, the payload changed, and it is byte-identical
        // to the cold run on the edited tree.
        recheck.Before.ShouldHaveViolationAtSites(CatchRule, ("source", ReportEndpoint), 1);
        recheck.After.ShouldHaveViolationAtSites(CatchRule, ("source", ReportEndpoint), 2);
        recheck.ShouldHaveMovedToTheColdAnswer();

        // …and the incremental store re-walked exactly the edited project (ReportEndpoint is in Web) plus its
        // reverse-dependent Domain.
        recheck.ReExtracted.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_WhenFilterAddedOnDisk_ClearsUnfilteredCatchRedWhileCatchBanStaysRedAndMatchesColdCli()
    {
        // Arrange — a warm server bound to the violated spec, where ReportEndpoint's ONE blanket catch is red
        // under two rules at once: the plain catch ban exceptions/no-general-catch (its site is unfiltered, so
        // both agree today) and the filter-aware exceptions/no-unfiltered-catch. This is the filter-fact analog
        // of the second-catch edit above, and the only place the filter fact crosses the dirty-rewalk path. The
        // filter-aware rule also reds ReportPublisher, whose unfiltered catch this edit never touches — the
        // constant that makes the ReportEndpoint drop legible.
        // Act — add a `when` filter to that same catch on disk, then re-check the still-warm server.
        WarmRecheck recheck = await ShouldRecheckAfterEditAsync(
            CliRunner.ViolatedSpecDll,
            "ReportEndpoint.cs",
            path => FixtureEdits.EditOnDisk(path, AddWhenFilterToSwallowingCatch));

        // Assert — one edit, two rules, opposite answers: the filter-aware rule drops ReportEndpoint entirely
        // (leaving only ReportPublisher, the Web layer's other broad catcher, which this edit never touched),
        // while the plain catch ban keeps ReportEndpoint red at the very same single site, because a `when`
        // filter never suppresses the catch edge — it only changes which of the edge's sites are recorded
        // unfiltered (GRAMMAR §4.8). And the warm answer is byte-identical to the cold one.
        recheck.Before.ShouldHaveFailedWith(
            "exceptions/no-unfiltered-catch", "source", [ReportEndpoint, "MyApp.Web.ReportPublisher"]);
        recheck.After.ShouldHaveFailedWith("exceptions/no-unfiltered-catch", "source", ["MyApp.Web.ReportPublisher"]);
        recheck.Before.ShouldHaveViolationAtSites(CatchRule, ("source", ReportEndpoint), 1);
        recheck.After.ShouldHaveViolationAtSites(CatchRule, ("source", ReportEndpoint), 1);
        recheck.ShouldHaveMovedToTheColdAnswer();

        // …and the incremental store re-walked exactly the edited project (ReportEndpoint is in Web) and nothing
        // else: the filter is inside a method body, so Domain's view of Web did not move.
        recheck.ReExtracted.ShouldBe([Web]);
    }

    [Fact]
    public async Task ArchCheck_SteadyStateNoDiskChange_ReadsAndReloadsNothing()
    {
        // Arrange — load, then promote every document past the racy window (backdate + one reconcile) so the
        // measured steady state is a pure O(stat) no-op (the steady-state case, driven through the tool).
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(fixture.SolutionPath, CliRunner.CleanSpecDll), Ct);

        var session = harness.Services.GetRequiredService<WorkspaceSession>();
        var store = harness.Services.GetRequiredService<SessionFragmentStore>();
        // Discover exactly as WarmSolutionSource does, so the direct session calls target the same path the
        // tool loads and never trip the "path changed => reload" branch.
        string solutionPath = ModelPipeline.DiscoverSolution(
            fixture.SolutionPath, Path.GetDirectoryName(fixture.SolutionPath)!,
            harness.Services.GetRequiredService<IEnvironment>());

        await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);
        WorkspaceSnapshot loaded = await session.GetCurrentAsync(solutionPath, Ct);
        // Backdating is best-effort: a file that cannot be re-stamped stays racy and re-reads, which the
        // warmup below captures into the baseline, before the measured window opens.
        FixtureEdits.BackdateAllDocuments(loaded);
        await session.GetCurrentAsync(solutionPath, Ct); // warmup: content-verifies + promotes every document
        long readsBefore = session.SweepContentReads;
        long reloadsBefore = session.FullReloadCount;

        // Act — two more arch_check calls with disk untouched.
        string first = await ShouldAnswerArchCheckAsync(harness);
        string second = await ShouldAnswerArchCheckAsync(harness);

        // Assert — the warm reconcile read no content and triggered no reload, the incremental store re-walked
        // nothing on the steady-state call, and the two responses are byte-identical.
        (session.SweepContentReads - readsBefore).ShouldBe(0);
        (session.FullReloadCount - reloadsBefore).ShouldBe(0);
        store.LastReExtractedProjects.ShouldBeEmpty();
        second.ShouldBeTheSameTextAs(first);
    }

    [Fact]
    public async Task ArchCheck_CsprojTouchedOnDisk_NextCallTripsExactlyOneReload()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(fixture.SolutionPath, CliRunner.CleanSpecDll), Ct);

        var session = harness.Services.GetRequiredService<WorkspaceSession>();
        await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct); // warm load
        long reloadsBefore = session.FullReloadCount;

        // Act — a structural touch: bump a project file's mtime, then re-check.
        FixtureEdits.StampForward(fixture.PathOf("MyApp.Domain", "MyApp.Domain.csproj"));
        CallToolResult after = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);

        // Assert — exactly one full reload, and the call still succeeded.
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        after.ShouldHaveAnswered();
    }

    [Fact]
    public async Task SolutionSource_WarmWorkspaceDisabled_ResolvesColdSource()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithCleanSpec(), Ct);

        // Act — flip the disable flag on the fake environment, then resolve the composed source. The factory
        // reads the flag lazily on first resolve, so setting it before this resolve selects the cold path.
        harness.Environment.SetVariable(SlideRuleEnvVars.DisableWarmWorkspace, "true");
        var source = harness.Services.GetRequiredService<ISolutionSource>();

        // Assert
        source.ShouldBeOfType<ColdSolutionSource>();
    }

    [Fact]
    public async Task TwoConcurrentToolCalls_BothSucceed()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);

        // Act — fire two tool calls at once. The MCP SDK dispatches them in parallel; the session gate
        // serializes the concurrent first-load, and both callers share the one immutable snapshot.
        Task<CallToolResult> first = harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)
            .AsTask();
        Task<CallToolResult> second = harness.Client.CallToolAsync("arch_status", cancellationToken: Ct)
            .AsTask();
        CallToolResult[] results = await Task.WhenAll(first, second);

        // Assert — both succeeded with content.
        results.Select(result => result.ShouldHaveAnswered())
            .ShouldAllBe(text => text.Length > 0);
    }

    [Fact]
    public async Task ArchCheck_SpecDllDeletedMidSession_ReturnsSameErrorAsColdCli()
    {
        // Arrange — bind to a throwaway copy of a spec DLL so deleting it cannot disturb the shared fixture.
        using TempDirectory temp = TestTempRoot.Fresh("warm-mcp-spec-copy");
        string tempSpec = temp.PathOf("CopiedSpec.dll");
        File.Copy(CliRunner.CleanSpecDll, tempSpec);

        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, tempSpec), Ct);

        // Warm the session WITHOUT loading the spec: arch_graph opens the workspace (it is spec-free), so
        // the spec DLL stays unlocked and can be deleted to model a mid-session removal.
        CallToolResult graph = await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct);
        graph.ShouldHaveAnswered();

        // Act — delete the bound spec DLL, then check against the still-warm workspace.
        File.Delete(tempSpec);
        CallToolResult check = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);
        CliResult coldCheck = await CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", tempSpec, "--json");
        coldCheck.ShouldRefuseWith();

        // Assert — the tool errors with exactly the text the cold CLI wrote to stderr in the same state.
        check.ShouldHaveRefused()
            .ShouldBeTheSameTextAs(coldCheck.Err);
    }

    [Fact]
    public async Task ArchCheck_SpecResolvedWarm_ReplaysUntilAStructuralChangeInvalidatesIt()
    {
        // Arrange — a warm server over the only fixture solution that DECLARES a spec project, bound by the
        // csproj that resolution has to walk the workspace to answer (the dogfooded shape: this repo's own
        // .mcp.json binds --spec <csproj>). A --spec <dll> binding resolves with no workspace at all and
        // would exercise none of this.
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        string specCsproj = workspace.PathOf(LayoutAppFixture.SpecProject, "LayoutApp.Spec.csproj");
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(workspace.SolutionPath, specCsproj), Ct);
        var resolutions = harness.Services.GetRequiredService<SpecResolutionCache>();

        // Act/Assert — the first call pays the walk once.
        string first = await ShouldAnswerArchCheckAsync(harness);
        resolutions.FullResolveCount.ShouldBe(1);
        first.ShouldHavePassed(LayoutAppFixture.RuleId);

        // …a second call with disk untouched replays it rather than walking again, and answers identically.
        string second = await ShouldAnswerArchCheckAsync(harness);
        resolutions.FullResolveCount.ShouldBe(1);
        second.ShouldBeTheSameTextAs(first);

        // …and so does a call after a source edit. A .cs edit mints a fresh snapshot per changed document
        // but no new load generation, the grain SpecResolutionCache keys on. The report still reflects the
        // edit (the new unprefixed interface reds the fixture's one rule) and still matches a freshly cold
        // run over the edited tree.
        FixtureEdits.AppendMemberLine(
            workspace.PathOf("LayoutApp.Core", "ILedger.cs"), "    public interface Reconciler { }");
        string edited = await ShouldAnswerArchCheckAsync(harness);
        CliResult coldEdited = await CliRunner.InvokeColdAsync(
            "check", workspace.SolutionPath, "--spec", specCsproj, "--no-cache", "--json");

        resolutions.FullResolveCount.ShouldBe(1);
        edited.ShouldHaveFailed(LayoutAppFixture.RuleId);
        edited.ShouldBeTheSameTextAs(coldEdited.Out);

        // Act/Assert — a structural touch reloads the workspace wholesale, which is the one change that can
        // move what resolution reads, so the next call walks again. The vector is the one
        // ArchCheck_CsprojTouchedOnDisk_NextCallTripsExactlyOneReload already proves trips exactly one reload.
        FixtureEdits.StampForward(workspace.PathOf("LayoutApp.Core", "LayoutApp.Core.csproj"));
        CallToolResult afterReload = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);

        resolutions.FullResolveCount.ShouldBe(2);
        afterReload.ShouldHaveAnswered();
    }

    [Fact]
    public async Task DeclaredMembership_ReadOncePerLoad_AndAgainWhenTheSolutionDropsAProject()
    {
        // Arrange — a warm server over a private copy, bound to a prebuilt spec DLL: arch_graph and a DLL-bound
        // arch_check are the two calls that read the solution's declared membership.
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(fixture.SolutionPath, CliRunner.CleanSpecDll), Ct);
        var source = harness.Services.GetRequiredService<ISolutionSource>()
            .ShouldBeOfType<WarmSolutionSource>();

        // Act/Assert — four calls over one load read membership once, and every project is declared.
        string first = (await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct)).ShouldHaveAnswered();
        (await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct)).ShouldHaveAnswered();
        await ShouldAnswerArchCheckAsync(harness);
        await ShouldAnswerArchCheckAsync(harness);

        source.MembershipReads.ShouldBe(1);
        DeclaredMembershipIn(first)
            .ShouldBe(new Dictionary<string, bool> { [Domain] = true, [Web] = true, [Billing] = true }, true);

        // Act — drop Billing from the solution file. Web still references it, so the reload loads it anyway.
        FixtureEdits.EditOnDisk(
            fixture.SolutionPath,
            solution => Regex.Replace(solution, @"Project\([^\n]*""MyApp\.Legacy\.Billing"",[^\n]*\n\s*EndProject\r?\n", ""));
        string afterEdit = (await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct)).ShouldHaveAnswered();

        // Assert — the reload read membership again, and the survey says Billing is no longer declared.
        source.MembershipReads.ShouldBe(2);
        DeclaredMembershipIn(afterEdit)
            .ShouldBe(new Dictionary<string, bool> { [Domain] = true, [Web] = true, [Billing] = false }, true);
    }

    [Fact]
    public async Task ArchCheck_SpecOutputDeletedAfterWarming_RefusesExactlyAsTheColdCliDoes()
    {
        // Arrange — the same declared-spec fixture, resolved by convention this time (the other branch that
        // has to walk the workspace), warmed with a real arch_check so the resolution is cached.
        // ArchCheck_SpecDllDeletedMidSession_ReturnsSameErrorAsColdCli does NOT cover this: it binds a DLL
        // and warms through spec-free arch_graph, so no resolution is ever recorded to go stale.
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(workspace.SolutionPath, null), Ct);
        var resolutions = harness.Services.GetRequiredService<SpecResolutionCache>();

        await ShouldAnswerArchCheckAsync(harness);
        resolutions.FullResolveCount.ShouldBe(1);

        // Act — delete the built spec output and re-check the still-warm server. The spec is loaded from
        // bytes (SpecLoadNoLockTests pins that), so the file is deletable mid-session.
        File.Delete(workspace.PathOf(
            LayoutAppFixture.SpecProject, "bin", "Debug", "net10.0", LayoutAppFixture.SpecAssembly));
        CallToolResult afterDelete = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);
        CliResult coldCheck = await CliRunner.InvokeColdAsync(
            "check", workspace.SolutionPath, "--no-cache", "--json");
        // Named, not merely "some refusal": byte-equality against a cold run that had refused for another
        // reason entirely — a spec project it could no longer find, say — would read exactly as green here.
        coldCheck.ShouldRefuseWith(
            $"The spec project '{LayoutAppFixture.SpecProject}' has no built output",
            "Build the solution first (dotnet build).");

        // Assert — only the candidate half is cached, so the built-output search still runs live on every
        // call and the refusal is byte-identical to the cold CLI's in the same state. And the cache was not
        // driven to a second walk to produce it: the replay refused, which is the point of the split.
        afterDelete.ShouldHaveRefused()
            .ShouldBeTheSameTextAs(coldCheck.Err);
        resolutions.FullResolveCount.ShouldBe(1);
    }

    [Fact]
    public async Task ArchCheck_CommentLinesInsertedOnDisk_RemapsSitesAndMatchesColdCli()
    {
        // Arrange — a warm server bound to the violated spec, whose rules red several sites inside
        // HomeController.cs, so the check document carries file:line positions that an inserted line moves.
        // Act — prepend two comment lines on disk, then re-check the still-warm server. Nothing the checker
        // reads changed, so this is the whole warm remap path end to end.
        WarmRecheck recheck = await ShouldRecheckAfterEditAsync(
            CliRunner.ViolatedSpecDll,
            "HomeController.cs",
            path => FixtureEdits.EditOnDisk(path, source => "// probe one\n// probe two\n" + source));

        // Assert — the moved sites are byte-identical to the ones a cold walk of the edited tree reports, and
        // the payload really did move (the red sites' lines are two lower than before the edit).
        recheck.ShouldHaveMovedToTheColdAnswer();

        // …and the store reached that answer without re-walking anything: Web's fragments were remapped, and
        // Domain, its reverse-dependent, was never touched.
        recheck.ReExtracted.ShouldBeEmpty();
        recheck.Remapped.ShouldBe([Web]);
    }

    // One edit-then-recheck case: a warm server over a private MyApp copy bound to specDll is checked, editOnDisk is
    // applied to the named Web file, and the server is checked again beside a cold CLI check of the edited tree, which
    // is the parity oracle. Every warm check must answer with one text block.
    private static async Task<WarmRecheck> ShouldRecheckAfterEditAsync(
        string specDll, string webFile, Action<string> editOnDisk)
    {
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(fixture.SolutionPath, specDll), Ct);
        var store = harness.Services.GetRequiredService<SessionFragmentStore>();

        string before = await ShouldAnswerArchCheckAsync(harness);
        editOnDisk(fixture.PathOf(Web, webFile));
        string after = await ShouldAnswerArchCheckAsync(harness);
        CliResult cold = await CliRunner.InvokeColdAsync(
            "check", fixture.SolutionPath, "--spec", specDll, "--json");

        return new WarmRecheck(before, after, cold.Out, store.LastReExtractedProjects, store.LastRemappedProjects);
    }

    // Each project of an arch_graph document by name, with whether the solution declares it.
    private static Dictionary<string, bool> DeclaredMembershipIn(string graphJson)
    {
        using JsonDocument document = JsonDocument.Parse(graphJson);
        return document.RootElement.GetProperty("projects")
            .EnumerateArray()
            .ToDictionary(
                project => project.GetProperty("name").GetString()!,
                project => project.GetProperty("solutionMember").GetBoolean(),
                StringComparer.Ordinal);
    }

    private static async Task<string> ShouldAnswerArchCheckAsync(McpPipelineHarness harness)
    {
        CallToolResult result = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);

        return result.ShouldHaveAnswered();
    }

    private sealed record WarmRecheck(
        string Before,
        string After,
        string ColdAfter,
        IReadOnlySet<string> ReExtracted,
        IReadOnlySet<string> Remapped)
    {
        // The edit moved the warm answer, to exactly what a cold run over the edited tree reports.
        internal void ShouldHaveMovedToTheColdAnswer()
        {
            After.ShouldNotBeTheSameTextAs(Before);
            After.ShouldBeTheSameTextAs(ColdAfter);
        }
    }

    // Turns HomeController's tokenless Save into one accepting a CancellationToken — the compliant signature,
    // so its member-shape red under async/accept-cancellation clears (its DocId changes as the parameter joins
    // the signature, and it drops out of the Task-returning subject's red set). Save has no callers, so the
    // signature change keeps MyApp.Web compiling.
    private static string AddCancellationTokenToSave(string source)
    {
        return source.Replace(
            "public System.Threading.Tasks.Task Save()",
            "public System.Threading.Tasks.Task Save(System.Threading.CancellationToken cancellationToken)");
    }

    // Flips IOrderFeed's registration from scoped to singleton in ServiceWiring — a singleton dependency injected
    // by a singleton is not captive, so the IOrderFeed edge drops out of di/no-captive-dependencies (GRAMMAR §4.7),
    // leaving only the transient IOrderFormatter red.
    private static string FlipOrderFeedToSingleton(string source)
    {
        return source.Replace("AddScoped<IOrderFeed, OrderFeed>", "AddSingleton<IOrderFeed, OrderFeed>");
    }

    // Adds a `when` filter to ReportEndpoint's blanket catch, using the method's own parameter so nothing new is
    // referenced. The catch edge and its §4.1 reference edge are minted exactly as before (GRAMMAR §4.8) — only
    // the recorded unfiltered-site set changes, which is what separates the two catch rules over this one site.
    private static string AddWhenFilterToSwallowingCatch(string source)
    {
        return source.Replace(
            "catch (System.Exception)",
            "catch (System.Exception) when (reportId > 0)");
    }
}
