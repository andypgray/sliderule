using System.Text.RegularExpressions;
using SlideRule.Rendering;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The checker the self-spec rule-count gate runs on. Prose that says how many rules govern this
///     repository is a hand-maintained number with a generated answer sitting beside it, so this lifts
///     every such claim out of a doc's committed bytes and counts the rule bullets the committed
///     <c>AGENTS.md</c> managed block actually renders.
/// </summary>
/// <remarks>
///     <para>
///         The rendered board is the authority because it is what a reader sees. A count of
///         <c>arch.Rule(</c> call sites would miss the rules the <c>DotNetGuidance</c> pack contributes and
///         the quarantined scope's bullet. A <c>check</c> report counts more rules than the board, because
///         a scope expands into rules the spec never spells, so a doc quoting a <c>Checked N rules</c>
///         summary is held by <see cref="ProseQuotedOutputTests" /> instead.
///     </para>
///     <para>
///         This reads the generated file git tracks, never the renderer, so the gate needs no workspace
///         and stays cheap, cross-platform and parallel-safe. That the committed <c>AGENTS.md</c> still
///         matches what the spec emits is proved by the dogfood self-spec tests and by CI re-rendering and
///         failing on any diff.
///     </para>
/// </remarks>
internal static class SpecRuleCounts
{
    /// <summary>
    ///     A rule bullet in a rendered managed block: <c>- `layering/core-no-roslyn` — …</c>. The
    ///     backticked id must carry at least one <c>/</c>, which is what keeps the layer bullets beside
    ///     them (<c>- **Core** — …</c>) out of the count without naming the sections to look in.
    /// </summary>
    private static readonly Regex RuleBullet = new(
        @"^- `[a-z0-9]+(-[a-z0-9]+)*(/[a-z0-9]+(-[a-z0-9]+)*)+` — ",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    ///     A prose claim about how many rules govern this repository. The number is captured as written —
    ///     a spelled word or a numeral — so the gate compares what the doc says rather than what a parse
    ///     of it rounded to.
    /// </summary>
    private static readonly Regex ClaimPattern = new(
        $@"\b{GrandfatheredCounts.CountToken} rules over this repository's real code\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    ///     Every rule bullet the managed block in <paramref name="agentsMarkdown" /> renders, across all
    ///     three posture sections. Reads the block body alone, so prose above or below the markers cannot
    ///     inflate it.
    /// </summary>
    public static int RenderedRuleCount(string agentsMarkdown)
    {
        string? body = ManagedBlock.ExtractBody(agentsMarkdown);
        if (body is null) return 0;

        return RuleBullet.Matches(body)
            .Count;
    }

    /// <summary>
    ///     Every self-spec rule-count claim in <paramref name="docText" />, named by
    ///     <paramref name="doc" />'s repo-relative path.
    /// </summary>
    public static IReadOnlyList<SpecRuleCountClaim> Extract(string doc, string docText)
    {
        List<SpecRuleCountClaim> claims = new();
        string[] lines = docText.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            foreach (Match match in ClaimPattern.Matches(line))
            {
                string written = match.Groups["count"]
                    .Value;

                // A delta reads "Four new rules over this repository's real code", so the token against
                // "rules" is the qualifier rather than the number, and the claim is a fact about one past
                // release rather than about the spec today. Requiring a number there is what tells a total
                // from a delta, and it is why the CHANGELOG needs no exemption entry.
                if (!GrandfatheredCounts.IsCountWord(written)) continue;

                claims.Add(new SpecRuleCountClaim(doc, index + 1, written));
            }
        }

        return claims;
    }
}
