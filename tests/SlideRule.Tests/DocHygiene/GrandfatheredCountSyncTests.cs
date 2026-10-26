using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The quote-sync gate over quoted grandfathered counts. The example walkthroughs and the self-check
///     page quote captured <c>check</c>, <c>status</c> and <c>baseline init</c> output, and every
///     ratcheted rule in those captures carries the size of its baseline — in the fenced capture, and
///     again in the prose that recaps it. Nothing regenerates a hand-written doc, so this gate holds each
///     number to the <c>entries</c> array of the committed baseline it came from: a re-baseline that
///     leaves a walkthrough behind fails the suite instead of publishing a page that contradicts the JSON
///     beside it.
/// </summary>
/// <remarks>
///     Sibling to <see cref="RuleQuoteSyncTests" />, which holds the rendered sentence in the same
///     captures against the <c>AGENTS.md</c> beside them, and to <see cref="WrittenPathSyncTests" />,
///     which holds their file lists against the tracked set. The three cover the three things a captured
///     report says: what the rule is, what it touched, and how much of it is on the record. Like them
///     this reads only committed bytes: no workspace load, no CLI invocation, so it stays cheap and
///     parallel-safe.
/// </remarks>
public sealed class GrandfatheredCountSyncTests
{
    private const string InlineSql = "data-access/no-inline-sql";
    private const string InjectClock = "time/inject-clock";
    private const string AsyncSuffix = "naming/async-suffix";
    private const string ClearanceContainment = "clearance/engine/containment";
    private const string DemurrageContainment = "demurrage/engine/containment";
    private const string NoSyncOverAsync = "async/no-sync-over-async";

    /// <summary>
    ///     The reader every baseline lookup below goes through. Its memo lives on the instance, so one held
    ///     here reads each card and baseline once for the whole class where a reader minted per call reads
    ///     them again every time.
    /// </summary>
    private static readonly Func<string, string?> ReadDiskText = RuleQuotes.DiskTextReader(RepoRoot.Directory);

    /// <summary>The count-quoting docs, each paired with the root whose baselines it quotes.</summary>
    private static readonly (string Doc, string ExampleRoot)[] CountDocs =
    [
        (DocPaths.MeridianReadme, ExampleRoots.Meridian),
        (DocPaths.MeridianAdopting, ExampleRoots.Meridian),
        (DocPaths.MeridianStoryboard, ExampleRoots.Meridian),
        (DocPaths.InterchangeReadme, ExampleRoots.Interchange),
        (DocPaths.OperationsReadme, ExampleRoots.Operations),
        // The self-check page quotes a check over this solution, but this repository grandfathers nothing
        // outside a quarantine, so the only counts it quotes are the Meridian ratchet its `As SARIF`
        // section is cut from.
        (DocPaths.SelfCheck, ExampleRoots.Meridian)
    ];

    private static readonly DocGate<GrandfatheredCount> Gate = new(
        CountDocs.Select(static entry => entry.Doc)
            .ToArray(),
        ExtractDoc,
        quotes: "grandfathered counts");

    /// <summary>
    ///     The prose sweep over the whole tracked corpus, held as a gate for its memo alone: two facts below
    ///     sweep every tracked markdown file, and between them that is one scan of the corpus rather than two.
    /// </summary>
    private static readonly DocGate<GrandfatheredCounts.ProseMention> Sweep = new(
        TrackedFiles.Markdown,
        SweepDoc,
        quotes: "grandfathered count mentions");

    /// <summary>
    ///     The prose recaps of those counts. A sentence is registered as the fragment it is written in
    ///     with the counts left as placeholders, plus the rules that fill them; the gate composes the
    ///     expected text from the committed baselines and asserts the doc contains it. Composition rather
    ///     than parsing is what makes a reworded sentence a failure — the number has moved out of the
    ///     shape this entry pins, and an entry that matches nothing holds nothing.
    /// </summary>
    private static readonly ProseCount[] ProseCounts =
    [
        new(DocPaths.MeridianReadme, ExampleRoots.Meridian,
            "{0} current violations are grandfathered",
            [GrandfatheredCounts.RootTotal],
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.MeridianReadme, ExampleRoots.Meridian,
            // A series mixing counts above and below ten reads as one list in digits.
            "{0} inline-SQL references, {1} wall-clock reads, {2} `Task`-returning methods missing the `Async` suffix, and {3} reach into the quarantined module",
            [InlineSql, InjectClock, AsyncSuffix, ClearanceContainment],
            GrandfatheredCounts.CountStyle.Numeral),
        new(DocPaths.MeridianReadme, ExampleRoots.Meridian,
            "the {0} grandfathered ones stay quiet",
            [InlineSql],
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.MeridianReadme, ExampleRoots.Meridian,
            "{0} reach into the module already exists",
            [ClearanceContainment],
            GrandfatheredCounts.CountStyle.TitleWord),
        new(DocPaths.MeridianAdopting, ExampleRoots.Meridian,
            // Step 5's refinement, and the one place the walkthrough states the curated rule's own count.
            "which leaves {0} grandfathered controller reads and one type doing its job",
            [InjectClock],
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.MeridianAdopting, ExampleRoots.Meridian,
            "holding {0} inline-SQL references, {1} clock reads, {2} unsuffixed methods, and {3} inbound reach",
            [InlineSql, InjectClock, AsyncSuffix, ClearanceContainment],
            GrandfatheredCounts.CountStyle.Numeral),
        new(DocPaths.MeridianAdopting, ExampleRoots.Meridian,
            "prints its grandfathered count ({0}, {1}, {2}, and {3})",
            [InlineSql, InjectClock, AsyncSuffix, ClearanceContainment],
            GrandfatheredCounts.CountStyle.Numeral),
        new(DocPaths.MeridianStoryboard, ExampleRoots.Meridian,
            // Beat 2: what the rendered block told the agent, and what made the name it chose debt.
            "the {0} inline-SQL references are grandfathered debt",
            [InlineSql],
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.MeridianStoryboard, ExampleRoots.Meridian,
            "a second ratchet with {0} sites already on the record",
            [AsyncSuffix],
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.MeridianStoryboard, ExampleRoots.Meridian,
            "is one of the {0}",
            [AsyncSuffix],
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.MeridianStoryboard, ExampleRoots.Meridian,
            // Beat 3 points at the captured board rather than restating the count in words.
            "counted in the same block as `grandfathered: {0}`",
            [AsyncSuffix],
            GrandfatheredCounts.CountStyle.Numeral),
        new(DocPaths.InterchangeReadme, ExampleRoots.Interchange,
            "the generated baseline contains {0} entries",
            [NoSyncOverAsync],
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.OperationsReadme, ExampleRoots.Operations,
            // The closing line of the quarantine argument: the count of grandfathered edges is the point.
            "{0} edge, one owner",
            [DemurrageContainment],
            GrandfatheredCounts.CountStyle.TitleWord),
        new(DocPaths.SelfCheck, ExampleRoots.Meridian,
            // The SARIF section's claim about the Meridian ratchet it is cut from.
            "Its {0} baselined violations keep the rule green",
            [InlineSql],
            GrandfatheredCounts.CountStyle.WordToTen)
    ];

    /// <summary>
    ///     Draft-era counts: sentences that deliberately state a number the committed baseline does not
    ///     hold, because the walkthrough is mid-authoring at that point. Each is pinned from both sides —
    ///     the drafted text must still be there, and the baseline's number must still be absent — so the
    ///     day the divergence closes, the exemption fails rather than quietly covering a real drift.
    /// </summary>
    private static readonly DraftCount[] DraftCounts =
    [
        // ADOPTING.md step 4 runs the blunt draft rule, before step 5 adds `.Except(SystemClock)`. The
        // draft flags the sanctioned seam alongside the controller reads, so its evidence is one higher
        // than the curated rule's baseline; step 5 walks through exactly that subtraction.
        new(DocPaths.MeridianAdopting, ExampleRoots.Meridian,
            "`time/inject-clock` reports eight sites",
            "`time/inject-clock` reports {0} sites",
            InjectClock,
            GrandfatheredCounts.CountStyle.Word),
        new(DocPaths.MeridianAdopting, ExampleRoots.Meridian,
            "| `time/inject-clock` | 8 clock reads |",
            "| `time/inject-clock` | {0} clock reads |",
            InjectClock,
            GrandfatheredCounts.CountStyle.Numeral),
        new(DocPaths.MeridianAdopting, ExampleRoots.Meridian,
            "The clock rule has eight sites",
            "The clock rule has {0} sites",
            InjectClock,
            GrandfatheredCounts.CountStyle.Word)
    ];

    /// <summary>
    ///     Prose the sweep flags that quotes no count: the word sits near a number that means something
    ///     else. Each entry must still match a swept line, so an exemption cannot outlive the sentence it
    ///     was written for.
    /// </summary>
    private static readonly (string Doc, string Marker, string Reason)[] SweepExemptions =
    [
        (DocPaths.DeriveSpecPrompt,
            "grandfathered in step 7)",
            "The 7 is the recipe's step number, not a count of anything baselined."),
        (DocPaths.DeriveSpecPrompt,
            "with the grandfathered counts visible",
            "The zeros are the re-check's expected exit code and `rulesFailed`, not a grandfathered count."),
        (DocPaths.MeridianReadme,
            "Three describe debt with a target",
            "The counts are of rules by posture — three law, three ratcheting, one quarantine — not of "
            + "the violations any of them grandfathers."),
        (DocPaths.MeridianReadme,
            "Move a controller onto a repository and its grandfathered count drops",
            "The zero is where a burndown ends, not a count the baseline holds today.")
    ];

    [Fact]
    public void QuotedGrandfatheredCounts_MatchTheirBaseline()
    {
        // Arrange
        List<string> drift = new();

        // Act: every fenced count, resolved against its own root's baselines, must equal the entries the
        // run it was captured from would have matched.
        foreach ((string doc, string exampleRoot) in CountDocs)
        foreach (GrandfatheredCount count in Gate.Scan(doc))
        {
            GrandfatheredCounts.CountResult result =
                GrandfatheredCounts.Classify(count, exampleRoot, TrackedFiles.All, ReadDiskText);
            if (result.Bucket != GrandfatheredCounts.CountBucket.Matched) drift.Add(result.Failure!);
        }

        // Assert
        drift.ShouldReportNothing("Quoted grandfathered counts no longer match the baselines they came from");
    }

    [Fact]
    public void EveryCountDoc_YieldsAtLeastOneCount()
    {
        // Act & Assert: guard against the scanner silently matching nothing if a doc's quoting style changes.
        Gate.ShouldYieldFromEveryRegisteredDoc();
    }

    [Fact]
    public void EveryTrackedDocQuotingFencedCounts_IsRegistered()
    {
        // Act & Assert: the registry above is hand-written, so the failure it cannot see is a doc that
        // quotes a count and was never added to it — a whole walkthrough silently outside the gate.
        Gate.ShouldFindNothingOutsideTheRegistry(
            counted: "grandfathered count(s)",
            swept: "grandfathered counts",
            authority: "a baseline");
    }

    [Fact]
    public void ProseCounts_ReadAsTheirBaselinesComposeThem()
    {
        // Arrange
        List<string> drift = new();

        // Act: compose each registered sentence from the committed baselines and require the doc to carry
        // it. Whitespace is collapsed first, because prose wraps and these fragments span line breaks.
        foreach (ProseCount prose in ProseCounts)
        {
            string? fragment = Compose(prose);
            if (fragment is null)
            {
                drift.Add($"{prose.Doc}: no committed baseline answers for [{string.Join(", ", prose.RuleIds)}] under {prose.ExampleRoot}.");
                continue;
            }

            if (HandCounts.CollapsedText(prose.Doc)
                    .Span(fragment) is null)
                drift.Add($"{prose.Doc} no longer reads '{fragment}', which its baselines compose from template '{prose.Template}'.");
        }

        // Assert
        drift.ShouldReportNothing("Prose recaps of grandfathered counts no longer match the baselines they came from");
    }

    [Fact]
    public void EveryProseCountMention_IsCoveredOrExempt()
    {
        // Arrange: the fenced sweep cannot see prose, so this is its counterpart — every unfenced mention
        // of grandfathering with a number beside it must be claimed by a registered sentence or carry a
        // written reason for being left out.
        List<string> uncovered = new();

        // Act
        foreach (string path in TrackedFiles.Markdown)
        {
            IReadOnlySet<int> claimed = ClaimedLines(path);
            foreach (GrandfatheredCounts.ProseMention mention in Sweep.Scan(path))
            {
                if (claimed.Contains(mention.DocLine) || IsExempt(mention)) continue;

                uncovered.Add($"{mention.Doc}:{mention.DocLine} states a grandfathered count no registry entry covers: {mention.Text.Trim()}");
            }
        }

        // Assert
        uncovered.ShouldReportNothing("These prose sites state a grandfathered count that nothing holds to a baseline");
    }

    [Fact]
    public void SweepExemptions_StillMatchASweptMention()
    {
        // Arrange
        GrandfatheredCounts.ProseMention[] swept = TrackedFiles.Markdown
            .SelectMany(Sweep.Scan)
            .ToArray();
        List<string> dead = new();

        // Act: an exemption whose sentence has been rewritten is no longer excusing anything, and leaving
        // it in place would hide the day that line starts quoting a real count.
        foreach ((string doc, string marker, string _) in SweepExemptions)
            if (!swept.Any(mention => mention.Doc == doc && mention.Text.Contains(marker, StringComparison.Ordinal)))
                dead.Add($"exemption {doc} -> '{marker}' matches no swept line.");

        // Assert
        dead.ShouldReportNothing("These sweep exemptions no longer correspond to any prose and should be removed");
    }

    [Fact]
    public void DraftCounts_StillDivergeFromTheirBaseline()
    {
        // Arrange
        List<string> failures = new();

        // Act: both halves of the pin. The drafted sentence must still be in the doc, and the baseline's
        // own number must still be absent from it — the day they agree, the reason this text is exempt has
        // gone away and it should join the registry above.
        foreach (DraftCount draft in DraftCounts)
        {
            GrandfatheredCounts.CollapsedText collapsed = HandCounts.CollapsedText(draft.Doc);
            if (collapsed.Span(draft.Fragment) is null) failures.Add($"{draft.Doc} no longer reads the drafted '{draft.Fragment}'.");

            int? expected = Expected(draft.ExampleRoot, draft.RuleId);
            if (expected is null)
            {
                failures.Add($"{draft.Doc}: no committed baseline answers for '{draft.RuleId}' under {draft.ExampleRoot}.");
                continue;
            }

            string composed = GrandfatheredCounts.Compose(draft.Template, [expected.Value], draft.Style);
            if (collapsed.Span(composed) is not null)
                failures.Add($"{draft.Doc} now reads '{composed}', which its baseline composes; the draft exemption is dead and the sentence should be registered.");
        }

        // Assert
        failures.ShouldReportNothing("Draft-era counts no longer diverge from the baselines they precede");
    }

    private static IReadOnlyList<GrandfatheredCount> ExtractDoc(string doc)
    {
        return GrandfatheredCounts.Extract(doc, RepoRoot.ReadText(doc));
    }

    private static IReadOnlyList<GrandfatheredCounts.ProseMention> SweepDoc(string doc)
    {
        return GrandfatheredCounts.ProseMentions(doc, RepoRoot.ReadText(doc));
    }

    private static int? Expected(string exampleRoot, string ruleId)
    {
        return GrandfatheredCounts.Expected(
            exampleRoot,
            ruleId,
            TrackedFiles.All,
            ReadDiskText);
    }

    private static string? Compose(ProseCount prose)
    {
        int?[] counts = prose.RuleIds
            .Select(ruleId => Expected(prose.ExampleRoot, ruleId))
            .ToArray();
        if (counts.Any(static count => count is null)) return null;

        return GrandfatheredCounts.Compose(
            prose.Template,
            counts.Select(static count => count!.Value)
                .ToArray(),
            prose.Style);
    }

    // The doc lines a registered prose sentence occupies — what makes the sweep's "covered" question
    // answerable by position, and what stops a second uncovered sentence hiding behind a first.
    private static IReadOnlySet<int> ClaimedLines(string doc)
    {
        // Asked for every tracked markdown file, and answered by a registered sentence for a handful of
        // them: nothing is claimed in a doc no entry names, so the collapse is not worth paying for.
        if (ProseCounts.All(entry => entry.Doc != doc)) return new HashSet<int>();

        GrandfatheredCounts.CollapsedText collapsed = HandCounts.CollapsedText(doc);
        HashSet<int> claimed = new();

        foreach (ProseCount prose in ProseCounts.Where(entry => entry.Doc == doc))
        {
            string? fragment = Compose(prose);
            if (fragment is null) continue;

            (int First, int Last)? span = collapsed.Span(fragment);
            if (span is null) continue;

            for (int line = span.Value.First; line <= span.Value.Last; line++) claimed.Add(line);
        }

        return claimed;
    }

    private static bool IsExempt(GrandfatheredCounts.ProseMention mention)
    {
        return SweepExemptions.Any(exemption =>
            exemption.Doc == mention.Doc && mention.Text.Contains(exemption.Marker, StringComparison.Ordinal));
    }

    /// <summary>
    ///     One registered prose recap: the <paramref name="Doc" /> it lives in, the
    ///     <paramref name="ExampleRoot" /> whose baselines answer for it, the <paramref name="Template" />
    ///     it is written in with <c>{0}</c>-style placeholders for its counts, the
    ///     <paramref name="RuleIds" /> that fill them in order, and the <paramref name="Style" /> the
    ///     sentence spells them in.
    /// </summary>
    private sealed record ProseCount(
        string Doc,
        string ExampleRoot,
        string Template,
        IReadOnlyList<string> RuleIds,
        GrandfatheredCounts.CountStyle Style);

    /// <summary>
    ///     One draft-era count: the <paramref name="Fragment" /> the doc must still carry, and the
    ///     <paramref name="Template" /> its <paramref name="RuleId" />'s baseline must still <em>not</em>
    ///     compose into it.
    /// </summary>
    private sealed record DraftCount(
        string Doc,
        string ExampleRoot,
        string Fragment,
        string Template,
        string RuleId,
        GrandfatheredCounts.CountStyle Style);
}
