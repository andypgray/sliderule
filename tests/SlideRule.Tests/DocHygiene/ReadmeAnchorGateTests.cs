using SlideRule.Tests.DocHygiene.Walkthroughs;
using SlideRule.Tests.Dogfood;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The walkthrough gate over quoted source anchors. The example walkthroughs, the landing page and
///     the self-check page quote <c>check</c> violation output inside fenced code blocks, and each quoted
///     line carries a <c>file:line</c> anchor into a committed source: an example's own tree for the
///     walkthroughs, this repository's tree for the self-check page's dogfood spine and for the hook beat
///     the landing page repeats from it. This gate lifts every anchor and holds it to its source: a
///     committed-source anchor must match the line it names, by content or by a landmark pin that keys a
///     documented walkthrough edit to the committed line it lands on; a demonstration anchor — check
///     output over a hypothetical edit that adds new code — must stay unmatched, so the day that code
///     lands the anchor graduates to a real one. A drifted anchor fails the suite instead of publishing
///     stale output.
/// </summary>
public sealed class ReadmeAnchorGateTests
{
    private const string BookingsController = WalkthroughScenarios.BookingsController;
    private const string CustomsController = "src/Meridian.Web/Controllers/CustomsController.cs";
    private const string ExpireQuotesHandler = WalkthroughScenarios.ExpireQuotesHandlerPath;
    private const string InvoicePreview = WalkthroughScenarios.InvoicePreviewPath;
    private const string RequestQuoteHandler = WalkthroughScenarios.RequestQuoteHandler;
    private const string ProgressPrinter = HookReportFenceTests.ProgressPrinterPath;

    /// <summary>The anchor-bearing docs, each paired with the root its anchors resolve against.</summary>
    private static readonly (string Doc, string ExampleRoot)[] AnchorDocs =
    [
        (DocPaths.MeridianReadme, ExampleRoots.Meridian),
        (DocPaths.MeridianAdopting, ExampleRoots.Meridian),
        (DocPaths.MeridianStoryboard, ExampleRoots.Meridian),
        (DocPaths.QuotingReadme, ExampleRoots.Quoting),
        (DocPaths.OperationsReadme, ExampleRoots.Operations),
        (DocPaths.InterchangeReadme, ExampleRoots.Interchange),
        // The self-check page quotes a self-check over this solution, and the landing page repeats one of
        // its stanzas, so both pages' anchors name this repository's own paths rather than an example's.
        (DocPaths.RootReadme, ExampleRoots.Self),
        (DocPaths.SelfCheck, ExampleRoots.Self)
    ];

    private static readonly DocGate<SourceAnchor> Gate = new(
        AnchorDocs.Select(static entry => entry.Doc)
            .ToArray(),
        SourceAnchors.Extract,
        quotes: "anchors",
        docs: "anchor docs");

    /// <summary>
    ///     Landmark pins for anchors the content bucket cannot verify: the reported line holds no copy of
    ///     the derived token, so the committed line the anchor is keyed to is pinned directly. Each entry
    ///     names the reported line and pins the committed line and a snippet that line must still carry.
    /// </summary>
    private static readonly Dictionary<SourceAnchors.AnchorKey, SourceAnchors.Landmark> Landmarks = new()
    {
        // http/reuse-httpclient inserts `using var probe = new HttpClient();` as the first statement of
        // CarrierClient.SendAsync; committed line 15 is the statement that insertion lands in front of.
        [new SourceAnchors.AnchorKey(ExampleRoots.Interchange, "src/Meridian.Interchange/Partners/CarrierClient.cs", 15)] = new SourceAnchors.Landmark(15, "StringContent content"),

        // di/hosted-services-scope-their-work swaps this constructor parameter to IOptionsSnapshot; the
        // primary-constructor declaration is committed line 14.
        [new SourceAnchors.AnchorKey(ExampleRoots.Interchange, "src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs", 14)] = new SourceAnchors.Landmark(14, "IOptions<InterchangeOptions> options"),

        // di/no-captive-dependencies swaps the first constructor parameter to IOutboxStore, adding a
        // using that shifts the declaration down; the primary-constructor declaration is committed line 14.
        [new SourceAnchors.AnchorKey(ExampleRoots.Interchange, "src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs", 15)] = new SourceAnchors.Landmark(14, "OutboxDispatcher(ScopedDispatchRunner runner"),

        // The same edit reads the outbox in the poll loop in place of the runner call, one line down for the
        // added using; the call it replaces is committed line 24.
        [new SourceAnchors.AnchorKey(ExampleRoots.Interchange, "src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs", 25)] = new SourceAnchors.Landmark(24, "await runner.RunPendingAsync(stoppingToken)"),

        // exceptions/no-general-catch wraps the per-message loop body in a catch-all; the inserted catch
        // is keyed to the committed loop at line 24.
        [new SourceAnchors.AnchorKey(ExampleRoots.Interchange, "src/Meridian.Interchange/Processing/OutboxProcessor.cs", 32)] = new SourceAnchors.Landmark(24, "foreach (OutboxMessage message in pending)"),

        // persistence/no-mapping-attributes adds a using and a [Table] attribute above the record; the
        // committed record declaration is line 7.
        [new SourceAnchors.AnchorKey(ExampleRoots.Interchange, "src/Meridian.Interchange/Outbox/OutboxMessage.cs", 10)] = new SourceAnchors.Landmark(7, "public sealed record OutboxMessage"),

        // contracts/no-entity-exposure changes the contract to take OutboxMessage, adding a using that
        // shifts this signature down; the committed signature is line 13.
        [new SourceAnchors.AnchorKey(ExampleRoots.Interchange, "src/Meridian.Interchange/Partners/IPartnerClient.cs", 15)] = new SourceAnchors.Landmark(13, "Task SendAsync(PartnerEnvelope envelope"),

        // The derive-time evidence pass reports these references at member-access lines, where
        // the type name is on the local's declaration rather than the reported line; the committed lines
        // are pinned directly.
        [new SourceAnchors.AnchorKey(ExampleRoots.Meridian, CustomsController, 28)] = new SourceAnchors.Landmark(28, "command.Parameters.AddWithValue"),
        [new SourceAnchors.AnchorKey(ExampleRoots.Meridian, CustomsController, 29)] = new SourceAnchors.Landmark(29, "connection.Open()"),
        [new SourceAnchors.AnchorKey(ExampleRoots.Meridian, CustomsController, 32)] = new SourceAnchors.Landmark(32, "command.ExecuteReader()"),
        [new SourceAnchors.AnchorKey(ExampleRoots.Meridian, CustomsController, 53)] = new SourceAnchors.Landmark(53, "validator.IsValid(number)"),

        // handlers/transactional deletes the [Transactional] attribute on committed line 13, shifting the
        // class declaration up to the reported line; the committed declaration is line 14.
        [new SourceAnchors.AnchorKey(ExampleRoots.Quoting, RequestQuoteHandler, 13)] = new SourceAnchors.Landmark(14, "public sealed class RequestQuoteHandler")
    };

    /// <summary>
    ///     Demonstration anchors: check output over a hypothetical edit that adds new code — a whole
    ///     method appended to a file, or a whole file dropped into a project — so there is no committed
    ///     line to anchor against. They must stay unmatched against committed source; the day such code
    ///     lands, the matching anchor here fails and is moved to the content or landmark bucket.
    /// </summary>
    private static readonly HashSet<(string Doc, string File, int Line)> DemonstrationAnchors =
    [
        // The statistical-prior and agent-loop walkthroughs add one inline-SQL method to the migrated
        // BookingsController, and the helpful-refactor walkthrough a method that reaches past the gateway;
        // the quoted lines are those methods' check output, which the walkthrough replay re-makes.
        (DocPaths.MeridianReadme, BookingsController, 75),
        (DocPaths.MeridianReadme, BookingsController, 77),
        (DocPaths.MeridianReadme, BookingsController, 78),

        // Beat 3 of the storyboard is a captured turn in which the agent inserted a lookup method above
        // BookingsController's sample endpoint; the quoted line is that method's declaration, which the
        // async-suffix ratchet reported and the replay reports again.
        (DocPaths.MeridianStoryboard, BookingsController, 60),

        // The hook beat quotes the block a red self-check feeds an agent: the hypothetical edit is a whole
        // new ProgressPrinter file dropped into the CLI's Rendering directory, which is not committed,
        // because this repository ships its own self-check green. The beat runs on the self-check page and
        // the landing page repeats the same stanza verbatim, so each page's pair is pinned.
        (DocPaths.RootReadme, ProgressPrinter, 10),
        (DocPaths.RootReadme, ProgressPrinter, 15),
        (DocPaths.SelfCheck, ProgressPrinter, 10),
        (DocPaths.SelfCheck, ProgressPrinter, 15),

        // The layering walkthrough drops a new ExpireQuotesHandler file into the Api project to breach the
        // Application boundary; that file is not committed, because the subsystem ships green.
        (DocPaths.QuotingReadme, ExpireQuotesHandler, 12),
        (DocPaths.QuotingReadme, ExpireQuotesHandler, 16),
        (DocPaths.QuotingReadme, ExpireQuotesHandler, 17),

        // The module-isolation walkthrough adds a new InvoicePreview file in Dispatch that constructs
        // Invoicing's internal assembler; that file is not committed, because the subsystem ships green.
        (DocPaths.OperationsReadme, InvoicePreview, 9)
    ];

    [Fact]
    public void CommittedSourceAnchors_MatchTheirCommittedLine()
    {
        // Arrange
        Func<string, IReadOnlyList<string>?> reader = RepoRoot.LineReader();
        List<string> drift = new();

        // Act: every anchor that is not a documented demonstration must resolve against committed source.
        foreach ((string doc, string exampleRoot) in AnchorDocs)
        foreach (SourceAnchor anchor in Gate.Scan(doc))
        {
            if (DemonstrationAnchors.Contains((doc, anchor.File, anchor.Line))) continue;

            SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, exampleRoot, Landmarks, reader);
            if (result.Bucket == SourceAnchors.AnchorBucket.Unresolved) drift.Add(result.Failure!);
        }

        // Assert
        drift.ShouldReportNothing("Quoted source anchors no longer match their committed source");
    }

    [Fact]
    public void DemonstrationAnchors_DoNotResolveAgainstCommittedSource()
    {
        // Arrange
        Func<string, IReadOnlyList<string>?> reader = RepoRoot.LineReader();
        List<string> promoted = new();

        // Act: a demonstration anchor quotes a hypothetical edit's output, so it must not match committed
        // source; if one starts matching, that code has landed and the anchor should become a real one.
        foreach ((string doc, string exampleRoot) in AnchorDocs)
        foreach (SourceAnchor anchor in Gate.Scan(doc))
        {
            if (!DemonstrationAnchors.Contains((doc, anchor.File, anchor.Line))) continue;

            SourceAnchors.AnchorResult result = SourceAnchors.Classify(anchor, exampleRoot, Landmarks, reader);
            if (result.Bucket != SourceAnchors.AnchorBucket.Unresolved) promoted.Add($"{doc} -> {anchor.File}:{anchor.Line} now matches committed source via {result.Bucket}; move it to the content or landmark bucket.");
        }

        // Assert
        promoted.ShouldReportNothing("Demonstration anchors now match committed source");
    }

    [Fact]
    public void EachAnchorDoc_YieldsAtLeastOneAnchor()
    {
        // Act & Assert: guard against the scanner silently matching nothing if a doc's quoting style changes.
        Gate.ShouldYieldFromEveryRegisteredDoc();
    }

    [Fact]
    public void EveryTrackedDocQuotingAnchors_IsRegistered()
    {
        // Act & Assert: the registry above is hand-written, so the failure it cannot see is a doc that
        // quotes anchors and was never added to it — a whole walkthrough silently outside the gate.
        Gate.ShouldFindNothingOutsideTheRegistry(
            counted: "source anchor(s)",
            swept: "source anchors",
            authority: "their committed source");
    }

    [Fact]
    public void FencedAnchorsOutsideSrc_AreEachHeldByACapturedFence()
    {
        // Arrange: the anchor reader takes src/ paths alone, so an anchor anywhere else is held by nothing
        // unless a captured fence re-runs the output it sits in. Every line those fences hold, by doc and line
        // number: by text, an anchor repeating a captured line in an uncaptured fence of the same doc would pass.
        HashSet<(string Doc, int Line)> captured = CapturedFences.All
            .SelectMany(static fence =>
            {
                QuotedFence quoted = fence.ShouldFindFence();
                return quoted.Lines.Select((_, i) => (fence.Doc, quoted.Line + i));
            })
            .ToHashSet();
        List<string> unheld = [];
        var found = 0;

        // Act
        foreach (string doc in TrackedFiles.Markdown)
        foreach ((string text, int number) in SourceAnchors.AnchorLinesOutsideSrc(RepoRoot.ReadText(doc)))
        {
            found++;
            if (!captured.Contains((doc, number)))
                unheld.Add($"{doc}:{number}: {text.Trim()}");
        }

        // A sweep that finds no anchor holds nothing, which is what a reshaped anchor grammar would look like.
        if (found == 0)
            unheld.Add("No tracked Markdown file carries a fenced source anchor outside src/, so this gate swept nothing.");

        // Assert
        unheld.ShouldReportNothing(
            "Fenced source anchors outside src/ that no captured fence re-runs, "
            + "so nothing holds them to their source");
    }

    [Fact]
    public void LandmarkAndDemonstrationEntries_AllMatchAnExtractedAnchor()
    {
        // Arrange
        HashSet<(string ExampleRoot, string File, int Line)> byKey = new();
        HashSet<(string Doc, string File, int Line)> byDoc = new();
        foreach ((string doc, string exampleRoot) in AnchorDocs)
        foreach (SourceAnchor anchor in Gate.Scan(doc))
        {
            byKey.Add((exampleRoot, anchor.File, anchor.Line));
            byDoc.Add((doc, anchor.File, anchor.Line));
        }

        // Act
        List<string> dead = new();
        foreach (SourceAnchors.AnchorKey key in Landmarks.Keys)
            if (!byKey.Contains((key.ExampleRoot, key.File, key.Line)))
                dead.Add($"landmark {key.ExampleRoot}/{key.File}:{key.Line} matches no extracted anchor.");

        foreach ((string Doc, string File, int Line) demonstration in DemonstrationAnchors)
            if (!byDoc.Contains(demonstration))
                dead.Add($"demonstration {demonstration.Doc} -> {demonstration.File}:{demonstration.Line} matches no extracted anchor.");

        // Assert
        dead.ShouldReportNothing("These pin entries no longer correspond to any anchor and should be removed");
    }
}
