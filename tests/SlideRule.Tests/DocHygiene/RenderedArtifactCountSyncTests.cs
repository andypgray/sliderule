using Shouldly;
using SlideRule.Rendering;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The quote-sync gate over prose claims about how much this repository's own render writes. Both
///     the root <c>AGENTS.md</c> and <c>CONTRIBUTING.md</c> open their dogfood paragraph by saying how
///     many artifacts the renderer produces and how many of them are cards, and nothing regenerates
///     those sentences, so this holds the numbers to the artifacts git carries: a rule that places a new
///     card without the prose following fails the suite instead of publishing a page that miscounts the
///     files sitting beside it.
/// </summary>
/// <remarks>
///     Sibling to <see cref="SpecCountSyncTests" />, which holds the claimed rule count to the rendered
///     board, and to <see cref="GrandfatheredCountSyncTests" />, which holds quoted baseline sizes to the
///     committed JSON. Like them this reads only committed bytes: no workspace load, no CLI invocation.
///     <see cref="RenderedArtifactCounts" /> carries why the committed set is the authority and what it
///     counts as an artifact.
/// </remarks>
public sealed class RenderedArtifactCountSyncTests
{
    private const string SourceRoot = "src/";

    /// <summary>The docs that claim a rendered-artifact count, in registration order.</summary>
    private static readonly string[] CountDocs = [ContextFileComposer.FileName, DocPaths.Contributing];

    private static readonly DocGate<RenderedArtifactCountClaim> Gate = new(
        CountDocs,
        ScanDoc,
        quotes: "rendered-artifact counts");

    /// <summary>
    ///     Every claim about the whole set spells the number of artifacts this repository commits. Both
    ///     sentences write the count mid-sentence, as a number word up to ten and digits above, so the
    ///     comparison is exact.
    /// </summary>
    [Fact]
    public void EveryClaimedArtifactCount_MatchesTheCommittedSet()
    {
        ShouldSpellTheCommittedCount(
            RenderedArtifactCounts.ClaimKind.Artifacts,
            RenderedArtifactCounts.CommittedArtifacts()
                .Count,
            "committed artifacts",
            "These docs state an artifact count the committed render contradicts");
    }

    /// <summary>Every claim about the cards alone spells the number of cards this repository commits.</summary>
    [Fact]
    public void EveryClaimedCardCount_MatchesTheCommittedCards()
    {
        ShouldSpellTheCommittedCount(
            RenderedArtifactCounts.ClaimKind.Cards,
            RenderedArtifactCounts.CommittedCards()
                .Count,
            "per-directory cards",
            "These docs state a card count the committed render contradicts");
    }

    /// <summary>
    ///     The scan still finds the artifacts. Without this the two gates above pass vacuously the day
    ///     the marker, the file name or the tracked-file scope moves: an empty set would compare against
    ///     docs that claim a number, and the failure would read as doc drift rather than as a blind
    ///     scanner.
    /// </summary>
    [Fact]
    public void CommittedArtifacts_StillYieldTheRootBlockAndItsCards()
    {
        RenderedArtifactCounts.CommittedArtifacts()
            .ShouldContain(ContextFileComposer.FileName,
                "the committed artifact set no longer holds this repository's own root block, so the scan has "
                + "gone blind and every count this gate holds is being compared against what is left");

        RenderedArtifactCounts.CommittedCards()
            .ShouldNotBeEmpty("the committed artifact set holds no cards, so the card half of this gate is "
                              + "comparing every claim against zero");
    }

    /// <summary>
    ///     Every card sits under <c>src/</c>, which is the locative half of the claim and the half a count
    ///     alone would not hold: a card placed in <c>arch/</c> or <c>tests/</c> keeps the number honest
    ///     while making the sentence false.
    /// </summary>
    [Fact]
    public void EveryCommittedCard_SitsUnderSrc()
    {
        RenderedArtifactCounts.CommittedCards()
            .Where(card => !card.StartsWith(SourceRoot, StringComparison.Ordinal))
            .ToList()
            .ShouldReportNothing(
                $"These committed cards sit outside {SourceRoot}, which {DocPaths.Contributing} and {ContextFileComposer.FileName} both "
                + "say is where every one of them lives — move the rule or reword the sentence");
    }

    /// <summary>Every registered doc still yields a claim — the guard against the scanner going blind.</summary>
    [Fact]
    public void EveryRegisteredDoc_YieldsAClaim()
    {
        Gate.ShouldYieldFromEveryRegisteredDoc();
    }

    /// <summary>
    ///     No tracked markdown outside the registry claims a rendered-artifact count. A third doc growing
    ///     the same sentence is the way this drift returns, and a hand-written registry cannot catch that
    ///     itself.
    /// </summary>
    [Fact]
    public void EveryTrackedDocClaimingAnArtifactCount_IsRegistered()
    {
        Gate.ShouldFindNothingOutsideTheRegistry(
            counted: "rendered-artifact count(s)",
            swept: "rendered-artifact counts",
            authority: "the committed render");
    }

    /// <summary>
    ///     The release history is quiet because it is exempt, not because the scanner stopped seeing it. Scanning the
    ///     same bytes yields the claims its release notes froze, so a reworded note turns this red rather than
    ///     leaving a dead exemption behind.
    /// </summary>
    [Fact]
    public void ReleaseHistory_IsSilencedByItsExemption_NotByTheScannerMissingIt()
    {
        string history = RepoRoot.ReadText(DocPaths.Changelog);

        Gate.Scan(DocPaths.Changelog)
            .ShouldBeEmpty("the release history records what past versions rendered; holding those numbers to "
                           + "today's set would rewrite history every time a rule places a card");

        RenderedArtifactCounts.Extract(DocPaths.Changelog, history)
            .ShouldNotBeEmpty("the scanner found nothing in the release history's bytes, so the exemption is dead "
                              + "weight and the scanner has stopped seeing the counts it was written for");
    }

    private static void ShouldSpellTheCommittedCount(
        RenderedArtifactCounts.ClaimKind kind,
        int committed,
        string noun,
        string header)
    {
        string expected = CountSpelling.Spell(committed, CountStyle.WordToTen);

        List<string> drift = CountDocs
            .SelectMany(Gate.Scan)
            .Where(claim => claim.Kind == kind)
            .Where(claim => !string.Equals(claim.Written, expected, StringComparison.Ordinal))
            .Select(claim =>
                $"{claim.Doc}:{claim.DocLine} claims '{claim.Written} {noun}' but this repository commits "
                + $"{committed} ('{expected}').")
            .ToList();

        drift.ShouldReportNothing(header);
    }

    // The release history is the one doc whose counts are frozen. A release note states what a past version
    // rendered ("Six per-directory cards are now committed where there was one"), and holding that to today's set
    // would rewrite history every time a rule places a card. Every other tracked doc is swept, and
    // ReleaseHistory_IsSilencedByItsExemption_NotByTheScannerMissingIt pins that this exemption, rather than a
    // pattern that stopped matching, is what keeps the file quiet.
    private static IReadOnlyList<RenderedArtifactCountClaim> ScanDoc(string doc, string text)
    {
        return doc == DocPaths.Changelog ? [] : RenderedArtifactCounts.Extract(doc, text);
    }
}
