using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The checker the hand-written count gates run on. A doc sentence that states how many of something
///     this repository has is a number nothing regenerates, so each one is registered as the prose
///     fragment it is written in, with a <c>{count}</c> placeholder where the number goes, and held to a
///     figure the repository can derive for itself. All of the logic lives here so both halves of the
///     registry — the one that needs a workspace and the one that does not — check a claim the same way.
/// </summary>
/// <remarks>
///     <para>
///         Matching exactly once is half the contract. A fragment that matches nothing is a gate
///         holding nothing: the sentence was reworded, the number went back to being hand-maintained, and
///         the suite stayed green. A fragment that matches twice is a number checked in one sentence and
///         free in the other. So zero matches and two matches are reported as failures beside a count
///         that disagrees, because all three are the same defect arriving by different routes.
///     </para>
///     <para>
///         A word where the number goes is not a count. The placeholder matches any word, so a
///         fragment written for "twenty declared projects" would also match "the declared projects" in a
///         sentence about something else. Only a match whose token is a number as this repository's prose
///         spells one is counted, which is what keeps a qualifier from reading as a claim.
///     </para>
/// </remarks>
internal static class HandCounts
{
    /// <summary>Where a registered fragment marks the number it claims.</summary>
    internal const string CountPlaceholder = "{count}";

    // One collapse per doc rather than one per claim: several docs carry more than one registered
    // sentence, and collapsing walks the whole file.
    private static readonly ConcurrentDictionary<string, GrandfatheredCounts.CollapsedText> Collapsed = new();

    /// <summary>
    ///     Every way <paramref name="doc" /> disagrees with <paramref name="authority" /> about
    ///     <paramref name="subject" />: the registered <paramref name="claim" /> matching other than once,
    ///     or matching once and spelling a different number in <paramref name="style" />. An empty list is
    ///     agreement.
    /// </summary>
    internal static IReadOnlyList<string> Drift(
        string doc,
        string claim,
        string subject,
        int authority,
        GrandfatheredCounts.CountStyle style)
    {
        GrandfatheredCounts.CollapsedText collapsed = CollapsedText(doc);
        List<Match> claimed = PatternFor(claim)
            .Matches(collapsed.Text)
            .Where(static match => GrandfatheredCounts.IsCountWord(Written(match)))
            .ToList();

        if (claimed.Count != 1)
            return
            [
                $"{doc} carries {claimed.Count} sentences matching '{claim}', not one. The claim about "
                + $"{subject} has moved or been duplicated, and the number there is held by nothing."
            ];

        Match only = claimed[0];
        string written = Written(only);
        string expected = GrandfatheredCounts.Spell(authority, style);
        if (string.Equals(written, expected, StringComparison.Ordinal)) return [];

        return
        [
            $"{doc}:{collapsed.LineAt(only.Index)} writes {subject} as '{written}', but it is {authority} "
            + $"('{expected}')."
        ];
    }

    /// <summary><paramref name="doc" />'s whitespace-collapsed text, read and collapsed once per run.</summary>
    internal static GrandfatheredCounts.CollapsedText CollapsedText(string doc)
    {
        return Collapsed.GetOrAdd(doc, static path => GrandfatheredCounts.Collapse(RepoRoot.ReadText(path)));
    }

    // The registered fragment as a pattern: its literal halves escaped, the placeholder replaced by the
    // count token every prose gate here captures a number with, so what counts as a number cannot differ
    // between one gate and the next.
    private static Regex PatternFor(string claim)
    {
        int placeholder = claim.IndexOf(CountPlaceholder, StringComparison.Ordinal);
        if (placeholder < 0)
            throw new InvalidOperationException(
                $"The registered claim '{claim}' marks no {CountPlaceholder}, so it claims no count.");

        string before = Regex.Escape(claim[..placeholder]);
        string after = Regex.Escape(claim[(placeholder + CountPlaceholder.Length)..]);

        return new Regex(before + GrandfatheredCounts.CountToken + after, RegexOptions.CultureInvariant);
    }

    private static string Written(Match match)
    {
        return match.Groups["count"]
            .Value;
    }
}
