using System.Globalization;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Hosting;
using SlideRule.Internal;

namespace SlideRule.Rendering;

/// <summary>
///     The one line a build finding prints: which rule broke, what broke it, why the rule exists, how to fix
///     it and the type to copy, in that order and separated by single spaces.
/// </summary>
/// <remarks>
///     <para>
///         One line, because the surface this feeds shows a warning's message on one row and truncates the
///         rest. The reader is therefore told everything at the site: the citation is the one thing left
///         out, being a link to follow rather than something to act on.
///     </para>
///     <para>
///         What broke the rule is worded by <see cref="ViolationWording" />, the human report's own owner,
///         so the report a developer reads and the warning the compiler raises name a violation in the same
///         words. The reason and the fix are the spec author's free text and are printed as written — never
///         trimmed, never re-cased — except that a sentence not already ended is given a full stop, so a
///         reason and the clause after it do not run together. The exemplar is printed as the spec names it,
///         never resolved: one project's compilation cannot say where a type in another project is declared.
///     </para>
///     <para>
///         The result holds no line break: every prose field a rule carries is single-line by construction
///         (<c>SingleLineProse</c>, enforced at spec build), and the wording is composed from names.
///     </para>
/// </remarks>
internal static class BuildFindingMessage
{
    /// <summary>
    ///     The line for <paramref name="violation" /> under <paramref name="result" />'s rule. Call it once
    ///     per violation: every site of one violation prints the same line, at a different place.
    /// </summary>
    /// <remarks>
    ///     <paramref name="result" /> rather than its rule alone, because whether the violation is a pair
    ///     that grew past its baseline is a fact about the run, and it is asked of
    ///     <c>RuleResult.GrownEntries</c> keyed by the very instance handed in.
    /// </remarks>
    internal static string Compose(RuleResult result, Violation violation)
    {
        ArchRule rule = result.Rule;
        var segments = new List<string> { $"{rule.Id}: {Wording(violation)}." };

        if (result.GrownEntries.TryGetValue(violation, out BaselineEntry entry)) segments.Add(GrownText(entry, violation));
        if (rule.Sentence.Length > 0) segments.Add(Terminated(rule.Sentence));
        segments.Add(Terminated($"Because: {rule.Because}"));
        if (rule.Fix is { } fix) segments.Add(Terminated($"Fix: {fix}"));
        if (rule.Exemplar is { } exemplar) segments.Add($"Exemplar: {exemplar}.");

        return string.Join(" ", segments);
    }

    // The same split the human report's site loop makes, for the same reason: the seven edge kinds read as a
    // source doing something to a far end, the three subject kinds as one thing being the wrong shape. The
    // site-less kinds never arrive — a finding needs a place — and EdgeText answers the empty string for
    // them rather than reaching through a slot they leave null.
    private static string Wording(Violation violation)
    {
        return violation.Kind switch
        {
            ViolationKind.Shape or ViolationKind.MemberShape or ViolationKind.ProjectShape =>
                ViolationWording.SubjectText(violation),
            _ => ViolationWording.EdgeText(violation)
        };
    }

    // Why a site the baseline names is red anyway: the allowance beside what this run measured. The noun
    // agrees with the allowance, which is the number the reader is being asked to compare against; an entry
    // with no recorded count reads as zero, which is what it tolerates once the pair is measured at all.
    private static string GrownText(BaselineEntry entry, Violation violation)
    {
        string baselined = Plurals.Counted(entry.SiteCount ?? 0, "site");
        var observed = violation.Sites.Count.ToString(CultureInfo.InvariantCulture);

        return $"The baseline holds {baselined} of this pair and {observed} were found.";
    }

    // A clause ended as a sentence, so the next one does not run into it. Question and exclamation marks end
    // a sentence too, and a reason written as a question reads wrong with a stop after it. Every caller
    // passes a segment that is non-empty by construction: a prefixed clause, or a sentence length-checked at
    // the call.
    private static string Terminated(string segment)
    {
        char last = segment[segment.Length - 1];
        return last is '.' or '!' or '?' ? segment : segment + ".";
    }
}
