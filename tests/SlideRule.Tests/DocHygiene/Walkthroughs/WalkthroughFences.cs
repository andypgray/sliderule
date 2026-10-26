using Shouldly;
using static SlideRule.Tests.DocHygiene.Walkthroughs.WalkthroughScenarios;

namespace SlideRule.Tests.DocHygiene.Walkthroughs;

/// <summary>
///     A walkthrough fence that quotes a run the replay re-makes: the <paramref name="Fence" /> and the marker
///     that picks it out, the <paramref name="Example" /> the run is over, the <paramref name="Scenario" /> it
///     imagines (null for the committed tree or the adoption walkthrough's steps), what the run prints as the
///     fence shows it (<paramref name="Expected" />, handed the fence's own lines for the one cut that is read off
///     the fence), and the sentence on the page that says what the fence leaves out (<paramref name="DeclaredCut" />,
///     null for a fence that shows the whole run).
/// </summary>
/// <remarks>
///     Asserting members, hence their names. Not attributed <c>[ShouldlyMethods]</c>: each red composes its own
///     message, which carries the doc line and the text to paste.
/// </remarks>
internal sealed record ReplayedFence(
    CapturedFence Fence,
    string Example,
    string? Scenario,
    Func<ExampleRun, IReadOnlyList<string>, string> Expected,
    string? DeclaredCut)
{
    /// <summary>
    ///     Asserts the fence is what <paramref name="run" /> prints, cut as the fence is cut and with line endings
    ///     folded, failing with the text to paste in its place.
    /// </summary>
    internal void ShouldMatchItsRun(ExampleRun run)
    {
        QuotedFence fence = ShouldFindFence(run);
        string quoted = FenceCuts.Folded(string.Join("\n", fence.Lines));
        string printed = FenceCuts.Folded(Expected(run, fence.Lines));
        if (string.Equals(quoted, printed, StringComparison.Ordinal)) return;

        throw new ShouldAssertException(
            $"{Fence.Doc}:{fence.Line} no longer matches the run; replace the fence body with:\n{printed}");
    }

    // The fence, or a red that also carries what the run prints: a fence that lost its marker is still the
    // fence a reader has to repair, and the run's text is what they will paste.
    private QuotedFence ShouldFindFence(ExampleRun run)
    {
        try
        {
            return Fence.ShouldFindFence();
        }
        catch (ShouldAssertException missing)
        {
            throw new ShouldAssertException($"{missing.Message} The run prints:\n{FenceCuts.Folded(Expected(run, []))}");
        }
    }
}

/// <summary>
///     A walkthrough fence showing the code a scenario writes: the <paramref name="Doc" /> it is on, its
///     <paramref name="Key" /> and <paramref name="Marker" />, the <paramref name="Scenario" /> whose edited files
///     it quotes, and the <paramref name="File" /> it quotes from until a label line names another.
/// </summary>
/// <remarks>
///     A null scenario is the one fence whose text is the input itself rather than an edit to a committed file:
///     the adoption walkthrough's draft spec, which the replay compiles as the page prints it.
/// </remarks>
internal sealed record EditFence(string Doc, string Key, string Marker, string? Scenario, string? File)
{
    /// <summary>The one fence of <see cref="Doc" /> carrying <see cref="Marker" />.</summary>
    internal QuotedFence ShouldFindFence()
    {
        return QuotedFence.ShouldFindOne(Doc, Marker, $"where the {Key} fence should be exactly one");
    }
}

/// <summary>
///     A walkthrough fence quoting what another tool printed, which the replay cannot make again: the
///     <paramref name="Doc" /> it is on, its <paramref name="Key" /> and <paramref name="Marker" />, why it is a
///     capture (<paramref name="Reason" />), and the words on its page that say so (<paramref name="Declaration" />).
/// </summary>
internal sealed record IllustrativeFence(string Doc, string Key, string Marker, string Reason, string Declaration);

/// <summary>
///     A walkthrough fence showing code for the reader to read that no committed file holds as shown: the
///     <paramref name="Doc" /> it is on, its <paramref name="Key" /> and <paramref name="Marker" />, and why no
///     source can hold it (<paramref name="Reason" />).
/// </summary>
internal sealed record ReadingFence(string Doc, string Key, string Marker, string Reason);

/// <summary>
///     The example walkthroughs' fences, each registered with how it is held. Every fence on those pages is in
///     exactly one registry here or in the quote-sync gate's, so a new fence cannot land outside a decision.
/// </summary>
/// <remarks>
///     A replayed fence is compared exactly with the run its scenario prints. Where it shows part of that run
///     the cut is applied to the run first, and the page states the cut in a sentence held beside it, because
///     an in-order excerpt passes a fence that lost lines nobody chose to drop.
/// </remarks>
internal static class WalkthroughFences
{
    private const string MeridianCheck = "sliderule check examples/Meridian/Meridian.slnx";

    // The declarations two pages make once for every check fence under them.
    private const string MeridianCheckCut = "cut to the rule that went red";
    private const string InterchangeCut = "cut to the rules that went red";

    private const string InterchangeLocalInlined =
        "Spells arch.Types.InNamespace(\"Meridian.Interchange.*\") where the spec uses its interchange local, so no "
        + "line is a line of the spec.";

    private static readonly string HeldBy =
        $"{nameof(WalkthroughReplayTests)}.{nameof(WalkthroughReplayTests.Fence_IsWhatItsScenarioPrints)}";

    /// <summary>The draft spec the adoption walkthrough checks in its step 4, compiled as the page prints it.</summary>
    internal static readonly EditFence AdoptingDraft = new(
        DocPaths.MeridianAdopting, "adopting-draft-spec", "DotNetGuidance.AsyncSuffix(arch, arch.AnyOf(domain, web), PackPosture.Enforce);",
        Scenario: null, File: null);

    /// <summary>Every fence the replay re-makes, page by page in the order each page prints them.</summary>
    internal static readonly IReadOnlyList<ReplayedFence> Replayed =
    [
        // The plain check skips the diff-aware tripwire and the hook's run does not, so the summary's skip
        // count is what tells the two inline-SQL fences apart.
        FromScenario(
            DocPaths.MeridianReadme, "meridian-sql-check", ", 1 skipped (", Sql,
            static run => FenceCuts.FailBlocksAndSummary(run.Report), cut: MeridianCheckCut + ", and the first block also keeps the summary line"),
        RuleBlockOf(DocPaths.MeridianReadme, "meridian-gateway-check", "FAIL clearance/engine/containment", Gateway, cut: MeridianCheckCut),
        FromExample(
            DocPaths.MeridianReadme, "meridian-status", "Burndown:", Meridian,
            static run => string.Join("\n", run.Tree.StatusLines)),
        FromScenario(
            DocPaths.MeridianReadme, "meridian-sql-hook", ", 0 skipped (", Sql,
            static run => FenceCuts.FailBlocksAndSummary(run.DiffReport), cut: "(its passing siblings in the report omitted here)"),

        FromExample(
            DocPaths.MeridianAdopting, "adopting-survey", "$ sliderule graph", Meridian,
            static run => FenceCuts.Transcript(
                "sliderule graph examples/Meridian/Meridian.slnx --json",
                FenceCuts.KeepingExternalEdges(
                    run.AdoptionSteps.Survey,
                    ("Meridian.Web", "Microsoft.Data"), ("Meridian.Web", "System.Data"), ("Meridian.Web", "System.Threading")),
                0),
            cut: "The capture is trimmed to three external roots, the two SQL-shaped ones plus `System.Threading`."),
        FromExample(
            DocPaths.MeridianAdopting, "adopting-draft-check", "because: Candidate:", Meridian,
            static run => FenceCuts.Transcript(
                MeridianCheck,
                FenceCuts.KeepingSitesIn(
                    run.AdoptionSteps.DraftReport, "data-access/no-inline-sql", "src/Meridian.Web/Controllers/CustomsController.cs"),
                1),
            cut: "the capture above shows CustomsController's five and drops the other five controllers"),
        FromExample(
            DocPaths.MeridianAdopting, "adopting-baseline-init", "$ sliderule baseline", Meridian,
            static run => FenceCuts.Transcript(
                "sliderule baseline init examples/Meridian/Meridian.slnx", string.Join("\n", run.AdoptionSteps.BaselineInit), 0)),
        FromExample(
            DocPaths.MeridianAdopting, "adopting-baselined-check", "pass data-access/no-inline-sql —", Meridian,
            static run => FenceCuts.Transcript(MeridianCheck, FenceCuts.WithoutLinesStarting(run.Tree.Report, "  grandfathered:"), 0),
            cut: "those sub-lines are dropped above"),
        FromExample(
            DocPaths.MeridianAdopting, "adopting-render", "$ sliderule render", Meridian,
            static run => FenceCuts.Transcript("sliderule render examples/Meridian/Meridian.slnx", string.Join("\n", run.RenderLines), 0)),

        FromScenario(DocPaths.MeridianStoryboard, "storyboard-beat3-report", "FAIL naming/async-suffix", Beat2, static run => run.DiffReport),
        FromScenario(
            DocPaths.MeridianStoryboard, "storyboard-beat4-summary", "(0 violations, 0 warnings)", Beat4,
            static run => FenceCuts.SummaryLine(run.DiffReport), cut: "only its summary line is shown here"),
        new(
            new CapturedFence(DocPaths.MeridianStoryboard, "storyboard-beat5-hook-document", "\"hookSpecificOutput\"", HeldBy),
            Meridian,
            Beat5.Key,
            static (run, fence) => FenceCuts.ContextCutAsQuoted(run.Scenario(Beat5.Key).StopHookDocument, fence),
            DeclaredCut: "is cut short here and printed in full below it"),
        FromScenario(
            DocPaths.MeridianStoryboard, "storyboard-beat5-context", "dragons: ISO 6346 check digit", Beat5,
            static run => run.DiffWithoutPasses),

        // The adapter fails a test with the rule's block, so this one is the whole message rather than a cut.
        RuleBlockOf(DocPaths.QuotingReadme, "quoting-transactional-message", "FAIL handlers/transactional", Transactional, cut: null),
        RuleBlockOf(DocPaths.QuotingReadme, "quoting-expire-check", "FAIL layering/application-boundaries", Expire, cut: "Here is the failing rule's block from its report"),

        FromExample(
            DocPaths.OperationsReadme, "operations-render", "unchanged AGENTS.md", Operations,
            static run => string.Join("\n", run.RenderLines)),
        FromScenario(
            DocPaths.OperationsReadme, "operations-preview-check", "FAIL modules/internals", Preview,
            static run => FenceCuts.FailBlocksAndSummary(run.Report), cut: "The report below keeps the two failures and the summary line"),
        FromExample(
            DocPaths.OperationsReadme, "operations-demurrage-status", "pass demurrage/engine/containment (quarantine)", Operations,
            static run => FenceCuts.StatusLine(run.Tree.StatusLines, "demurrage/engine/containment"),
            cut: "the quarantine's line reads"),

        RuleBlockOf(DocPaths.InterchangeReadme, "interchange-probe-check", "FAIL http/reuse-httpclient", Probe, cut: InterchangeCut),
        FromExample(
            DocPaths.InterchangeReadme, "interchange-status", "Burndown:", Interchange,
            static run => string.Join("\n", run.Tree.StatusLines)),
        FromScenario(
            DocPaths.InterchangeReadme, "interchange-captive-check", "FAIL di/no-captive-dependencies", Captive,
            static run => FenceCuts.FailBlocks(run.Report), cut: InterchangeCut),
        RuleBlockOf(DocPaths.InterchangeReadme, "interchange-catch-check", "FAIL exceptions/no-general-catch", Catch, cut: InterchangeCut),
        RuleBlockOf(DocPaths.InterchangeReadme, "interchange-token-check", "FAIL async/accept-cancellation", Token, cut: InterchangeCut),
        RuleBlockOf(DocPaths.InterchangeReadme, "interchange-table-check", "FAIL persistence/no-mapping-attributes", Table, cut: InterchangeCut),
        RuleBlockOf(DocPaths.InterchangeReadme, "interchange-contract-check", "FAIL contracts/no-entity-exposure", Contract, cut: InterchangeCut),
        RuleBlockOf(
            DocPaths.InterchangeReadme, "interchange-snapshot-check", "references Microsoft.Extensions.Options.IOptionsSnapshot",
            Snapshot, cut: InterchangeCut),
        RuleBlockOf(DocPaths.InterchangeReadme, "interchange-rename-check", "FAIL naming/async-suffix", Rename, cut: InterchangeCut)
    ];

    /// <summary>Every fence showing the code a scenario writes, and the draft spec the replay compiles.</summary>
    internal static readonly IReadOnlyList<EditFence> Edits =
    [
        AdoptingDraft,
        new(DocPaths.MeridianStoryboard, "storyboard-beat2-edit", "GetByReference(string reference)", Beat2.Key, "BookingsController.cs"),
        new(DocPaths.MeridianStoryboard, "storyboard-beat4-edit", "GetByReferenceAsync(string reference)", Beat4.Key, "BookingsController.cs"),
        new(DocPaths.MeridianStoryboard, "storyboard-beat5-edit", "is not ('U' or 'J' or 'Z')", Beat5.Key, "ContainerNumberValidator.cs"),
        new(DocPaths.InterchangeReadme, "interchange-probe-edit", "using var probe = new HttpClient();", Probe.Key, "CarrierClient.cs"),
        new(DocPaths.InterchangeReadme, "interchange-captive-edit", "internal sealed class OutboxDispatcher(IOutboxStore store", Captive.Key, "OutboxDispatcher.cs"),
        new(DocPaths.InterchangeReadme, "interchange-catch-edit", "// Keep draining the batch", Catch.Key, "OutboxProcessor.cs"),
        new(DocPaths.InterchangeReadme, "interchange-token-edit", "// ScopedDispatchRunner.cs", Token.Key, "ScopedDispatchRunner.cs"),
        new(DocPaths.InterchangeReadme, "interchange-table-edit", "[Table(\"InterchangeOutbox\")]", Table.Key, "OutboxMessage.cs")
    ];

    /// <summary>
    ///     The capture of the adapter's test run on the clean-architecture walkthrough, whose timings no second
    ///     run reproduces.
    /// </summary>
    internal static readonly IllustrativeFence QuotingAdapterRun = new(
        DocPaths.QuotingReadme, "quoting-adapter-run", "Test run summary: Passed!",
        "The test runner's own layout and wall-clock timings, captured from a real run of the adapter project: the "
        + "workspace load billed to whichever case runs first, and the run's duration. The timings are the point of "
        + "the paragraph under it, and no two runs print the same ones.",
        "captured from a real run");

    /// <summary>Every fence quoting what another tool printed, each declared as a capture on its page.</summary>
    internal static readonly IReadOnlyList<IllustrativeFence> Illustrative =
    [
        new(
            DocPaths.MeridianAdopting, "adopting-sln-add", "$ dotnet sln",
            "The .NET SDK's own output, which SlideRule does not print and cannot re-run.",
            "quoted here as the .NET SDK printed them"),
        new(
            DocPaths.MeridianAdopting, "adopting-git-status", "$ git status",
            "git's report after a walk the reader performs. The baselines it reports unchanged are held by the replayed "
            + "capture's byte comparison, and the solution-file churn is the SDK's.",
            "as `git status` reported it after one such walk"),
        QuotingAdapterRun,
        new(
            DocPaths.MeridianStoryboard, "storyboard-stop-hook-feedback", "Stop hook feedback:",
            "Claude Code's own rendering of the refused stop, quoted from the session transcript.",
            "Claude Code's line are quoted from the session transcripts as captured")
    ];

    /// <summary>Every fence showing code for the reader to read, which no committed file holds as shown.</summary>
    internal static readonly IReadOnlyList<ReadingFence> Reading =
    [
        new(
            DocPaths.MeridianAdopting, "adopting-empty-spec", "// The rules arrive in step 3.",
            "The spec class before its rules; every other line of it is also a line of the draft the replay compiles."),
        new(
            DocPaths.OperationsReadme, "operations-module-rules", "arch.Rule(\"modules/internals\")",
            "The spec's module rules with its Selection locals written inline, so no line is a line of the spec."),
        new(
            DocPaths.InterchangeReadme, "interchange-accept-cancellation", "DotNetGuidance.AcceptCancellation(",
            InterchangeLocalInlined),
        new(
            DocPaths.InterchangeReadme, "interchange-mapping-attributes", "DotNetGuidance.NoMappingAttributes(",
            InterchangeLocalInlined),
        new(
            DocPaths.InterchangeReadme, "interchange-entity-exposure-rule", "arch.Rule(\"contracts/no-entity-exposure\")",
            InterchangeLocalInlined)
    ];

    /// <summary>The replayed fence keyed <paramref name="key" />.</summary>
    internal static ReplayedFence ReplayedNamed(string key)
    {
        return Replayed.SingleOrDefault(fence => string.Equals(fence.Fence.Key, key, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"No replayed walkthrough fence is keyed '{key}'.");
    }

    /// <summary>The edit fence keyed <paramref name="key" />.</summary>
    internal static EditFence EditNamed(string key)
    {
        return Edits.SingleOrDefault(fence => string.Equals(fence.Key, key, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"No walkthrough edit fence is keyed '{key}'.");
    }

    /// <summary>Every marker these registries hold a fence to, with the doc it names a fence in.</summary>
    internal static IEnumerable<(string Doc, string Key, string Marker)> Markers()
    {
        return Replayed.Select(static fence => (fence.Fence.Doc, fence.Fence.Key, fence.Fence.Marker))
            .Concat(Edits.Select(static fence => (fence.Doc, fence.Key, fence.Marker)))
            .Concat(Illustrative.Select(static fence => (fence.Doc, fence.Key, fence.Marker)))
            .Concat(Reading.Select(static fence => (fence.Doc, fence.Key, fence.Marker)));
    }

    // A fence cut from one scenario's run.
    private static ReplayedFence FromScenario(
        string doc, string key, string marker, WalkthroughScenario scenario, Func<ScenarioRun, string> expected,
        string? cut = null)
    {
        return new ReplayedFence(
            new CapturedFence(doc, key, marker, HeldBy), scenario.Example, scenario.Key,
            (run, _) => expected(run.Scenario(scenario.Key)), cut);
    }

    // The block check prints for the one rule a scenario breaks, which is also a failing test's message.
    private static ReplayedFence RuleBlockOf(string doc, string key, string marker, WalkthroughScenario scenario, string? cut)
    {
        return FromScenario(doc, key, marker, scenario, run => run.RuleBlock(scenario.Moves.Single()), cut);
    }

    // A fence cut from the example's committed tree, or from the adoption walkthrough's steps over it.
    private static ReplayedFence FromExample(
        string doc, string key, string marker, string example, Func<ExampleRun, string> expected, string? cut = null)
    {
        return new ReplayedFence(new CapturedFence(doc, key, marker, HeldBy), example, null, (run, _) => expected(run), cut);
    }
}
