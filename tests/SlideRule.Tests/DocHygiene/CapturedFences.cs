using Shouldly;
using SlideRule.Tests.Dogfood;
using SlideRule.Tests.Legacy;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     One fence on a public page that quotes tool output rather than a committed file: the
///     <paramref name="Doc" /> it sits in, its <paramref name="Key" />, the <paramref name="Marker" /> that
///     picks it out of that doc's fences, and the test that re-runs the tool and holds the fence to what the
///     run prints (<paramref name="HeldBy" />).
/// </summary>
/// <remarks>
///     Asserting members, hence their names. Deliberately not attributed <c>[ShouldlyMethods]</c>: every red
///     here composes its own message, which carries the doc line and the text to paste.
/// </remarks>
internal sealed record CapturedFence(string Doc, string Key, string Marker, string HeldBy)
{
    /// <summary>
    ///     The one fence of <see cref="Doc" /> carrying <see cref="Marker" />, with the doc line its body
    ///     starts on.
    /// </summary>
    internal QuotedFence ShouldFindFence()
    {
        return QuotedFence.ShouldFindOne(Doc, Marker, $"where {HeldBy} re-runs exactly one, the {Key} fence");
    }

    /// <summary>
    ///     Asserts the fence's body is <paramref name="live" /> exactly, line endings aside, failing with the
    ///     live text as the body to paste in its place.
    /// </summary>
    internal void ShouldMatchTheRun(string live)
    {
        QuotedFence fence = ShouldFindFence();
        string quoted = string.Join("\n", fence.Lines);
        string run = live.NormalizedLines();
        if (string.Equals(quoted, run, StringComparison.Ordinal)) return;

        throw new ShouldAssertException(
            $"{Doc}:{fence.Line} no longer matches the run; replace the fence body with:\n{run}");
    }

    /// <summary>
    ///     Asserts every non-blank line of the fence is a whole line of <paramref name="output" />, in order,
    ///     failing with the first line that is not and the run's own <paramref name="context" />.
    /// </summary>
    /// <remarks>
    ///     For a fence that deliberately shows part of a run: the lines it leaves out need no counterpart,
    ///     and the lines it keeps must be the run's own, each whole.
    /// </remarks>
    internal void ShouldBeAnInOrderExcerptOf(string output, string context)
    {
        QuotedFence fence = ShouldFindFence();
        string[] printed = output.NormalizedLines()
            .Split('\n');

        var searchFrom = 0;
        foreach (string line in fence.Lines.Where(static line => !string.IsNullOrWhiteSpace(line)))
        {
            int found = Array.IndexOf(printed, line, searchFrom);
            if (found < 0)
                throw new ShouldAssertException(
                    $"{Doc}:{fence.Line} quotes '{line}', which is not a line of the run after the "
                    + $"fence's previous one.\n{context}");

            searchFrom = found + 1;
        }
    }
}

/// <summary>
///     A fence's body as the page holds it: the doc <paramref name="Line" /> the body starts on and its
///     <paramref name="Lines" />, verbatim.
/// </summary>
internal sealed record QuotedFence(int Line, IReadOnlyList<string> Lines)
{
    /// <summary>
    ///     The one fence of <paramref name="doc" /> carrying <paramref name="marker" />, failing with how many
    ///     carry it and <paramref name="expectation" />, which says what should hold that one fence.
    /// </summary>
    /// <remarks>Asserting, hence the name.</remarks>
    internal static QuotedFence ShouldFindOne(string doc, string marker, string expectation)
    {
        List<IReadOnlyList<(string Text, int Number)>> carrying = SourceAnchors.FencedBlocks(RepoRoot.ReadText(doc))
            .Where(candidate => candidate.Any(line => line.Text.Contains(marker, StringComparison.Ordinal)))
            .ToList();

        if (carrying is not [var block])
            throw new ShouldAssertException($"{doc} carries {carrying.Count} fences marked '{marker}' {expectation}.");

        return new QuotedFence(
            block[0].Number,
            block.Select(static line => line.Text)
                .ToList());
    }
}

/// <summary>
///     The fences on the public pages that quote a run rather than a committed file, each with the test that
///     re-runs it. A run the page no longer matches reds in that test, printing the fence to paste.
/// </summary>
/// <remarks>
///     Registered here rather than in <see cref="QuoteSyncTests" />, whose rows each name a committed source:
///     these have none, and the holder is what stands in for it. The quote-sync gate still holds each marker
///     to exactly one fence.
/// </remarks>
internal static class CapturedFences
{
    private const string HookReportKey = "hook-report";

    private const string HookReportMarker = "FAIL cli/no-stdout";

    private static readonly string HookReportHolder =
        $"{nameof(HookReportFenceTests)}.{nameof(HookReportFenceTests.HookReportFence_IsTheStanzaTheImaginedChangeDraws)}";

    /// <summary>The stanza a red self-check hands an agent through the hook.</summary>
    internal static readonly CapturedFence SelfCheckHookReport =
        new(DocPaths.SelfCheck, HookReportKey, HookReportMarker, HookReportHolder);

    /// <summary>The landing page's copy of the same stanza.</summary>
    internal static readonly CapturedFence ReadmeHookReport =
        new(DocPaths.RootReadme, HookReportKey, HookReportMarker, HookReportHolder);

    /// <summary>
    ///     An excerpt of <c>explain --type</c> over this solution: the claims on the CLI's command shell, each line
    ///     a line of the answer, in order.
    /// </summary>
    internal static readonly CapturedFence SelfCheckClaims = new(
        DocPaths.SelfCheck,
        "explain-type",
        "it is named `CommandEntryPoint`",
        $"{nameof(SelfCheckClaimsFenceTests)}.{nameof(SelfCheckClaimsFenceTests.ClaimsFence_IsAnExcerptOfWhatExplainPrintsForTheShell)}");

    /// <summary>
    ///     One result object from <c>check --sarif</c> over Meridian. The <c>"ruleId":</c> spelling is what
    ///     keeps this marker off the check document below, which spells the same rule <c>"id":</c>.
    /// </summary>
    internal static readonly CapturedFence SarifResult = new(
        DocPaths.SelfCheck,
        "sarif-result",
        "\"ruleId\": \"data-access/no-inline-sql\"",
        $"{nameof(SelfCheckMeridianFenceTests)}.{nameof(SelfCheckMeridianFenceTests.SarifResultFence_IsTheResultAFreshCheckWrites)}");

    /// <summary>The same rule's entry from <c>check --json</c> over Meridian.</summary>
    internal static readonly CapturedFence CheckDocument = new(
        DocPaths.SelfCheck,
        "check-json",
        "\"posture\": \"migrate\"",
        $"{nameof(SelfCheckMeridianFenceTests)}.{nameof(SelfCheckMeridianFenceTests.CheckDocumentFence_IsTheRuleEntryAFreshCheckWrites)}");

    /// <summary>The project lines <c>graph</c> prints for this solution's drawn projects.</summary>
    internal static readonly CapturedFence GraphSurvey = new(
        DocPaths.SelfCheck,
        "graph-survey",
        "references: (none)",
        $"{nameof(SelfSpecTests)}.{nameof(SelfSpecTests.SelfCheckGraphFence_IsWhatTheSurveyPrintsForTheDrawnProjects)}");

    /// <summary>
    ///     A stanza from a check over the non-SDK-style ClassicApp fixture: an excerpt of the run, which only
    ///     a Windows machine with a Framework build host can make.
    /// </summary>
    internal static readonly CapturedFence LegacyCheck = new(
        DocPaths.RootReadme,
        "legacy-check",
        "Classic.Billing/BillingCalculator.cs:10",
        $"{nameof(ClassicProjectCheckTests)}.{nameof(ClassicProjectCheckTests.Check_NonSdkFrameworkSolution_ExitsOneWithTheDesignedVerdictsAndFileLine)}");

    /// <summary>
    ///     Every captured fence: the self-check page's in the order it prints them, then the landing page's, then
    ///     the command-line reference's help.
    /// </summary>
    internal static readonly IReadOnlyList<CapturedFence> All =
    [
        SelfCheckHookReport,
        SelfCheckClaims,
        SarifResult,
        CheckDocument,
        GraphSurvey,
        ReadmeHookReport,
        LegacyCheck,
        .. CliHelpFences.Captured
    ];
}
