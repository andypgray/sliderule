using System.Globalization;
using ModelContextProtocol.Protocol;
using Shouldly;
using SlideRule.Cli.Mcp;
using SlideRule.Cli.Mcp.Pipeline;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     The acceptance table: every <c>arch_*</c> tool returns output identical (after newline
///     normalization) to the CLI verb it shells over — parity by construction (each tool runs the same
///     runner into a captured writer).
/// </summary>
/// <remarks>
///     <para>
///         Each method starts one harness over one solution+spec binding, and several share the violated
///         spec's. The rule-ID explain rows ride the DLL fast path (no workspace), while the type and path
///         rows extract the codebase their claims are traced over. <c>arch_context</c> is pinned against the
///         render card text, and the <c>diffBase</c> row mirrors <see cref="TripwireDiffE2ETests" /> over a
///         real git repo.
///     </para>
///     <para>
///         The filter brackets each call with the shared
///         <see cref="SlideRule.Cli.Mcp.Infrastructure.IdleTimeoutWatchdog" /> in-flight counter.
///         Nothing here reads it: the suites that read or reset it are in the
///         <see cref="SerialCollection">"Serial"</see> collection, which starts only once this class has
///         finished.
///     </para>
///     <para>
///         The narrowing rows extend the same contract to the knobs — each tool argument produces exactly
///         what its CLI option produces — and the two budget rows cover the one behaviour with no CLI
///         spelling: over the client's declared response budget, <c>arch_graph</c> and <c>arch_check</c>
///         each re-render one rung coarser, and what comes back is byte-identical to what <c>--grain</c>
///         writes at that rung. Each walks the whole ladder, not one step of it: a budget between any two adjacent
///         rungs returns the coarser one's own document, down to the floor. That identity is the whole
///         claim, because it is what makes a degraded answer a complete document rather than a cut one. The
///         narrowing rows also hold the parse itself to parity: a rung in any case, and the one refusal for a word
///         off the ladder.
///     </para>
///     <para>
///         The CLI side of every row runs <see cref="CliRunner.InvokeColdAsync(string[])" />, not the warm-by-default
///         <see cref="CliRunner.InvokeAsync(string[])" />, so the reference side never reads the warm session the
///         harness holds. The harness these rows compare against is warm, so this is the suite's warm-against-cold
///         net; serving both sides from one pooled workspace would make it compare the warm path with itself. The
///         cold side is fronted by the run's persisted cache, so a leg may answer as a hit or a closure partial over
///         fragments an earlier cold leg wrote for the same path, a path the golden suite holds to a fresh load.
///     </para>
/// </remarks>
public sealed class CliMcpParityTests
{
    // The four cold graph documents this suite measures against — one per rung of the grain ladder — each
    // produced once. Several rows want more than one of them, and the solution behind them is never mutated
    // here — the diff-base row edits its own TempGitRepo copy — so the same document answers every row.
    // Memoizing the task does not warm anything: the run underneath is still CliRunner.InvokeColdAsync, which
    // never reads the harness's warm session, as the remarks above require; it is awaited more than once instead
    // of re-run.
    private static readonly Lazy<Task<CliResult>> ColdGraph =
        new(() => CliRunner.InvokeColdAsync("graph", CliRunner.MyAppSolution, "--json"));

    private static readonly Lazy<Task<CliResult>> ColdGraphOverview =
        new(() => CliRunner.InvokeColdAsync("graph", CliRunner.MyAppSolution, "--json", "--grain", "overview"));

    private static readonly Lazy<Task<CliResult>> ColdGraphSkeleton =
        new(() => CliRunner.InvokeColdAsync("graph", CliRunner.MyAppSolution, "--json", "--grain", "skeleton"));

    private static readonly Lazy<Task<CliResult>> ColdGraphIndex =
        new(() => CliRunner.InvokeColdAsync("graph", CliRunner.MyAppSolution, "--json", "--grain", "index"));

    // The four cold check documents, memoized for the same reason and against the same spec every row here
    // binds to. Nothing mutates the solution behind them either — the diff-base row runs against its own
    // TempGitRepo copy — so the same full document answers the parity row and the budget row alike.
    private static readonly Lazy<Task<CliResult>> ColdCheckFull =
        new(() => CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json"));

    private static readonly Lazy<Task<CliResult>> ColdCheckOverview =
        new(() => CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--grain", "overview"));

    private static readonly Lazy<Task<CliResult>> ColdCheckSkeleton =
        new(() => CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--grain", "skeleton"));

    private static readonly Lazy<Task<CliResult>> ColdCheckIndex =
        new(() => CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--grain", "index"));

    [Fact]
    public async Task HarnessA_ViolatedSpec_CheckStatusExplain_MatchCli()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);

        // arch_check ≡ check --json (CLI exits 1 on the violation; the tool never reports IsError). The
        // ViolatedSpec carries every violation kind including the member-subject rule naming/async-suffix
        // (memberShape / subjectMember, GRAMMAR §4.6), so this byte-parity covers member subjects too.
        CliResult cliCheck = await ColdCheckFull.Value;
        cliCheck.ShouldReportViolations();
        CallToolResult mcpCheck = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);
        mcpCheck.ShouldHaveAnswered()
            .ShouldBeTheSameTextAs(cliCheck.Out);

        // arch_status ≡ status --json.
        CliResult cliStatus = await CliRunner.InvokeColdAsync(
            "status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json");
        CallToolResult mcpStatus = await harness.Client.CallToolAsync("arch_status", cancellationToken: Ct);
        mcpStatus.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliStatus.Out);

        // arch_graph ≡ graph --json (spec-independent; the survey ignores the bound spec, and graph takes no --spec).
        CliResult cliGraph = await ColdGraph.Value;
        CallToolResult mcpGraph = await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct);
        mcpGraph.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliGraph.Out);

        // arch_explain <known> ≡ explain <known> stdout.
        CliResult cliExplain = await CliRunner.InvokeColdAsync(
            "explain", "layering/domain-independent", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        CallToolResult mcpExplain = await harness.Client.CallToolAsync(
            "arch_explain", new Dictionary<string, object?> { ["ruleId"] = "layering/domain-independent" }, cancellationToken: Ct);
        mcpExplain.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliExplain.Out);

        // arch_explain <unknown> IsError text ≡ explain <unknown> stderr (CLI exit 2).
        CliResult cliUnknown = await CliRunner.InvokeColdAsync(
            "explain", "unknown/id", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        cliUnknown.ShouldRefuseWith();
        CallToolResult mcpUnknown = await harness.Client.CallToolAsync(
            "arch_explain", new Dictionary<string, object?> { ["ruleId"] = "unknown/id" }, cancellationToken: Ct);
        mcpUnknown.ShouldHaveRefused()
            .ShouldBeTheSameTextAs(cliUnknown.Err);

        // arch_explain type ≡ explain --type, and path ≡ explain --path: the claims answer, byte for byte.
        CliResult cliType = await CliRunner.InvokeColdAsync(
            "explain", "--type", "OrderService", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        cliType.ShouldSucceed("MyApp.Domain.OrderService (class)");
        CallToolResult mcpType = await harness.Client.CallToolAsync(
            "arch_explain", new Dictionary<string, object?> { ["type"] = "OrderService" }, cancellationToken: Ct);
        mcpType.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliType.Out);

        CliResult cliPath = await CliRunner.InvokeColdAsync(
            "explain", "--path", "MyApp.Legacy.Billing", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        cliPath.ShouldSucceed("MyApp.Legacy.Billing.BillingFacade (class)");
        CallToolResult mcpPath = await harness.Client.CallToolAsync(
            "arch_explain", new Dictionary<string, object?> { ["path"] = "MyApp.Legacy.Billing" }, cancellationToken: Ct);
        mcpPath.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliPath.Out);

        // An unknown type is the same refusal on both surfaces.
        CliResult cliUnknownType = await CliRunner.InvokeColdAsync(
            "explain", "--type", "OrderServ", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        cliUnknownType.ShouldRefuseWith("Unknown type 'OrderServ'");
        CallToolResult mcpUnknownType = await harness.Client.CallToolAsync(
            "arch_explain", new Dictionary<string, object?> { ["type"] = "OrderServ" }, cancellationToken: Ct);
        mcpUnknownType.ShouldHaveRefused()
            .ShouldBeTheSameTextAs(cliUnknownType.Err);
    }

    [Fact]
    public async Task HarnessJ_ExplainWithOtherThanOneForm_IsRefusedNamingTheToolsOwnParameters()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);

        CallToolResult none = await harness.Client.CallToolAsync(
            "arch_explain", new Dictionary<string, object?>(), cancellationToken: Ct);
        CallToolResult two = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = "layering/domain-independent", ["type"] = "OrderService" },
            cancellationToken: Ct);

        // The CLI's refusal names its own spellings (a positional and two options), so the tool states the
        // same rule in its parameters' names rather than quoting the CLI.
        none.ShouldHaveRefused()
            .ShouldBe("Pass exactly one of ruleId, type and path.");
        two.ShouldHaveRefused()
            .ShouldBe(McpServerBinding.ExplainFormsRefusal);
    }

    [Fact]
    public async Task HarnessB_CleanSpec_Check_MatchesCli()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithCleanSpec(), Ct);

        CliResult cliCheck = await CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.CleanSpecDll, "--json");
        cliCheck.ShouldSucceed();
        CallToolResult mcpCheck = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);

        mcpCheck.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliCheck.Out);
    }

    [Fact]
    public async Task HarnessC_RenderSpec_Context_InScopeCardAndOutOfScopePointer()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.RenderSpecDll), Ct);

        // A path inside the quarantined scope → that scope's card body.
        CallToolResult inScope = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Legacy.Billing/BillingCalculator.cs" }, cancellationToken: Ct);
        inScope.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(RenderedLawText.BillingScopeCard);

        // A path inside the cautioned scope → that scope's card body. Same tool, same spec, the other scope
        // posture: what an agent reads before editing dragon territory does not depend on whether the
        // dragons are fenced off or merely cautioned.
        CallToolResult inCaution = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Domain/RetryPolicy.cs" }, cancellationToken: Ct);
        inCaution.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(RenderedLawText.DomainCautionCard);

        // A path no scope covers → the pinned pointer line (echoing the query path). The RenderSpec's
        // Domain/Web layers carry no anchored rules, so no layer card competes here.
        CallToolResult outScope = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Web/HomeController.cs" }, cancellationToken: Ct);
        outScope.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(
                "No architecture scope covers 'MyApp.Web/HomeController.cs'. Architecture context for this solution lives in " +
                "the root AGENTS.md managed block; expand any rule with 'sliderule explain <rule-id>'.");
    }

    [Fact]
    public async Task HarnessD_QuarantinedSpec_CheckDiffBase_MatchesCliAndWarnsTripwire()
    {
        using var repo = new TempGitRepo();
        // A brand-new untracked file in dragon territory — the tripwire's agent-hook case.
        repo.WriteQuarantineNote();

        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(repo.SolutionPath, CliRunner.QuarantinedSpecDll), Ct);

        CliResult cliCheck = await CliRunner.InvokeColdAsync(
            "check", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--json", "--diff-base", "HEAD");
        CallToolResult mcpCheck = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["diffBase"] = "HEAD" }, cancellationToken: Ct);

        string mcpText = mcpCheck.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliCheck.Out);
        mcpText.ShouldContain("quarantinedScopeTouched");
    }

    [Fact]
    public async Task HarnessD_QuarantinedSpec_CheckEmptyDiffBase_AnswersAsIfNoneWasGiven()
    {
        // A client that fills every optional argument sends an empty diff base, which names no ref.
        using var repo = new TempGitRepo();
        repo.WriteQuarantineNote();

        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(repo.SolutionPath, CliRunner.QuarantinedSpecDll), Ct);

        CallToolResult withoutDiffBase = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?>(), cancellationToken: Ct);
        CallToolResult emptyDiffBase = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["diffBase"] = "" }, cancellationToken: Ct);

        emptyDiffBase.ShouldHaveAnswered()
            .ShouldBeTheSameTextAs(withoutDiffBase.ShouldHaveAnswered());
    }

    [Fact]
    public async Task HarnessE_LayerSpec_Context_InLayerCardAndOutOfScopePointer()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.LayerSpecDll), Ct);

        // A path inside the Web layer directory → the local-rules cards of both layers placed there.
        CallToolResult inLayer = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Web/HomeController.cs" }, cancellationToken: Ct);
        inLayer.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(RenderedLawText.WebLayerCards);

        // A path no layer or quarantined scope covers → the pointer line (echoing the query path).
        CallToolResult outScope = await harness.Client.CallToolAsync(
            "arch_context", new Dictionary<string, object?> { ["path"] = "MyApp.Domain/Order.cs" }, cancellationToken: Ct);
        outScope.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(
                "No architecture scope covers 'MyApp.Domain/Order.cs'. Architecture context for this solution lives in " +
                "the root AGENTS.md managed block; expand any rule with 'sliderule explain <rule-id>'.");
    }

    [Fact]
    public async Task HarnessF_NarrowedCalls_MatchTheirCliTwins()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);

        // arch_graph overview ≡ graph --grain overview --json: the whole survey at coarser grain.
        CliResult cliOverview = await ColdGraphOverview.Value;
        CallToolResult mcpOverview = await harness.Client.CallToolAsync(
            "arch_graph", new Dictionary<string, object?> { ["grain"] = "overview" }, cancellationToken: Ct);
        mcpOverview.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliOverview.Out);

        // arch_graph skeleton ≡ graph --grain skeleton --json: the structural spine, external rows as a count.
        CliResult cliSkeleton = await ColdGraphSkeleton.Value;
        CallToolResult mcpSkeleton = await harness.Client.CallToolAsync(
            "arch_graph", new Dictionary<string, object?> { ["grain"] = "skeleton" }, cancellationToken: Ct);
        mcpSkeleton.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliSkeleton.Out);

        // A rung written in another case is that rung on both surfaces: the document is the lowercase flag's.
        CliResult cliCased = await CliRunner.InvokeColdAsync(
            "graph", CliRunner.MyAppSolution, "--json", "--grain", "Skeleton");
        cliCased.ShouldSucceed();
        cliCased.ShouldPrint(cliSkeleton.Out);
        CallToolResult mcpCased = await harness.Client.CallToolAsync(
            "arch_graph", new Dictionary<string, object?> { ["grain"] = "Skeleton" }, cancellationToken: Ct);
        mcpCased.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliSkeleton.Out);

        // A word off the ladder is refused with one text on both surfaces, before either loads a workspace.
        const string refusal = "Unknown grain 'coarse'; use full, overview, skeleton or index.";
        CliResult cliOff = await CliRunner.InvokeColdAsync(
            "graph", CliRunner.MyAppSolution, "--json", "--grain", "coarse");
        cliOff.ShouldRefuseWithExactly(refusal);
        CallToolResult mcpOff = await harness.Client.CallToolAsync(
            "arch_graph", new Dictionary<string, object?> { ["grain"] = "coarse" }, cancellationToken: Ct);
        mcpOff.ShouldHaveRefused()
            .ShouldBe(refusal);

        // arch_graph projects ≡ graph --projects --json: the same survey over fewer projects.
        CliResult cliScoped = await CliRunner.InvokeColdAsync(
            "graph", CliRunner.MyAppSolution, "--json", "--projects", "MyApp.Web");
        CallToolResult mcpScoped = await harness.Client.CallToolAsync(
            "arch_graph", new Dictionary<string, object?> { ["projects"] = "MyApp.Web" }, cancellationToken: Ct);
        mcpScoped.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliScoped.Out);

        // arch_check rules ≡ check --rules --json (CLI exits 1 on the subset's violations; the tool never
        // reports IsError). Same parse, same selection, same document — including rulesFilter.
        CliResult cliRules = await CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--rules", "exceptions/*");
        cliRules.ShouldReportViolations();
        CallToolResult mcpRules = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["rules"] = "exceptions/*" }, cancellationToken: Ct);
        mcpRules.ShouldHaveAnswered()
            .ShouldBeTheSameTextAs(cliRules.Out);

        // arch_check overview ≡ check --grain overview --json: the whole report with each violation's sites as a
        // count.
        CliResult cliCheckOverview = await ColdCheckOverview.Value;
        CallToolResult mcpCheckOverview = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["grain"] = "overview" }, cancellationToken: Ct);
        mcpCheckOverview.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliCheckOverview.Out);

        // arch_check skeleton ≡ check --grain skeleton --json: the verdict alone, violations as a count.
        CliResult cliCheckSkeleton = await ColdCheckSkeleton.Value;
        CallToolResult mcpCheckSkeleton = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["grain"] = "skeleton" }, cancellationToken: Ct);
        mcpCheckSkeleton.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliCheckSkeleton.Out);

        // The floor rung is a caller's flag on both surfaces, not a degrade-only state. It has to be: a rung
        // the server can drop a caller onto but the caller cannot ask for is one no CLI reader can reproduce,
        // and reproducing a degraded answer at its own grain is the property this whole file exists for.
        //
        // arch_graph index ≡ graph --grain index --json: the project roster, edges as a count.
        CliResult cliGraphIndex = await ColdGraphIndex.Value;
        CallToolResult mcpGraphIndex = await harness.Client.CallToolAsync(
            "arch_graph", new Dictionary<string, object?> { ["grain"] = "index" }, cancellationToken: Ct);
        mcpGraphIndex.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliGraphIndex.Out);

        // arch_check index ≡ check --grain index --json: a verdict per rule ID, the prose gone.
        CliResult cliCheckIndex = await ColdCheckIndex.Value;
        CallToolResult mcpCheckIndex = await harness.Client.CallToolAsync(
            "arch_check", new Dictionary<string, object?> { ["grain"] = "index" }, cancellationToken: Ct);
        mcpCheckIndex.ShouldHaveTextContent()
            .ShouldBeTheSameTextAs(cliCheckIndex.Out);
    }

    [Fact]
    public async Task HarnessG_GraphOverTheResponseBudget_ReturnsExactlyTheCliDocumentForTheGrainItLandsOn()
    {
        await ShouldAnswerEachRungWithThatGrainsCliDocumentAsync(
            "arch_graph", [ColdGraph, ColdGraphOverview, ColdGraphSkeleton, ColdGraphIndex],
            result => result.ShouldSucceed());
    }

    [Fact]
    public async Task HarnessH_CheckOverTheResponseBudget_ReturnsExactlyTheCliDocumentForTheGrainItLandsOn()
    {
        // HarnessG's twin, and the row that matters most of the two: arch_check is the tool agents are told
        // to call before finishing work, and its bulk driver is an uncapped per-site dump, so this is the
        // response most likely to overrun on the codebases the product is for. A cut report there evicts an
        // agent from the tool surface entirely, which is the failure the ladder exists for.
        await ShouldAnswerEachRungWithThatGrainsCliDocumentAsync(
            "arch_check",
            [ColdCheckFull, ColdCheckOverview, ColdCheckSkeleton, ColdCheckIndex],
            result => result.ShouldReportViolations());
    }

    /// <summary>
    ///     Walks <paramref name="tool" /> down the grain ladder a rung at a time and asserts that each answer
    ///     is byte-identical to the CLI document for the grain it landed on: over a budget between two
    ///     adjacent <paramref name="rungs" /> the tool must return the whole coarser document — nothing cut,
    ///     so the JSON still parses and the client reads a whole answer rather than half of one.
    /// </summary>
    /// <remarks>
    ///     Every budget is derived from the CLI documents either side of the rung under test rather than
    ///     guessed, so this proves the degrade instead of assuming a fixture size — and a rung added below
    ///     the last one is covered by appending it to <paramref name="rungs" />, never by re-picking a
    ///     literal. All the calls share one harness on purpose: the tool re-reads the budget per call, so a
    ///     tighter one takes effect without a second server, and no call names a grain — the degrade is the
    ///     server's own decision.
    /// </remarks>
    /// <param name="tool">The tool to call, finest rung first.</param>
    /// <param name="rungs">The CLI documents, one per rung, ordered finest to coarsest.</param>
    /// <param name="verdict">The exit contract each CLI leg must meet.</param>
    private static async Task ShouldAnswerEachRungWithThatGrainsCliDocumentAsync(
        string tool,
        IReadOnlyList<Lazy<Task<CliResult>>> rungs,
        Action<CliResult> verdict)
    {
        // Arrange
        List<CliResult> documents = [];
        foreach (Lazy<Task<CliResult>> rung in rungs)
        {
            CliResult document = await rung.Value;
            verdict(document);
            documents.Add(document);
        }

        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);

        // Act & Assert — one pass per step down the ladder. The claim is ladder-wide, not overview-shaped:
        // what comes back is whatever grain the response landed on, spelled exactly as that grain's own flag
        // spells it.
        for (var step = 1; step < documents.Count; step++)
        {
            int tokens = ShouldHaveBudgetBetween(documents[step - 1], documents[step]);
            harness.Environment.SetVariable(
                SlideRuleEnvVars.MaxMcpOutputTokens, tokens.ToString(CultureInfo.InvariantCulture));

            // The plain call, with no grain argument.
            CallToolResult degraded = await harness.Client.CallToolAsync(tool, cancellationToken: Ct);

            string text = degraded.ShouldHaveTextContent();
            text.ShouldNotContain("--- RESPONSE TRUNCATED ---");
            text.ShouldBeTheSameTextAs(documents[step].Out);
        }

        // A degrade is the server's own decision, not a failure, so nothing on the way down is logged.
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    /// <summary>
    ///     The client-declared token budget that lands between <paramref name="larger" /> and
    ///     <paramref name="smaller" />, asserting that the cap it converts to really does sit at or above the
    ///     document that should survive the truncator whole and below the one that should overrun.
    /// </summary>
    private static int ShouldHaveBudgetBetween(CliResult larger, CliResult smaller)
    {
        int largerChars = larger.Out.Length;
        int smallerChars = smaller.Out.Length;
        // Working back from characters to the token budget a client would declare: at two characters per token,
        // this many tokens puts the cap near the midpoint of the two documents. The two assertions below are what
        // keep that honest if the truncator's rule ever moves.
        int tokens = (largerChars + smallerChars) / 4;
        int budget = ResponseTruncator.ComputeMaxChars(tokens.ToString(CultureInfo.InvariantCulture));

        budget.ShouldBeGreaterThanOrEqualTo(smallerChars);
        budget.ShouldBeLessThan(largerChars);

        return tokens;
    }
}
