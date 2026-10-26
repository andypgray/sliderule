using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Model;

namespace SlideRule.Rendering;

/// <summary>
///     A check report projected into the warnings a build should print: one per offending site a caller owns.
/// </summary>
/// <remarks>Placement is <see cref="ViolationPlacement" />'s, shared with the human report.</remarks>
internal static class BuildFindings
{
    /// <summary>
    ///     The findings <paramref name="report" /> produces for the files <paramref name="ownsFile" /> admits,
    ///     in the report's rule order, then each result's violation order, then each violation's site order.
    ///     Nothing is sorted here.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Red violations only: a grandfathered violation, a warning, a skipped rule and a passing rule
    ///         all yield nothing. A violation with no site yields nothing either, having nowhere to point.
    ///     </para>
    ///     <para>
    ///         <paramref name="ownsFile" /> is asked of a site's path exactly as the violation carries it, and
    ///         for a shape violation it is asked of the first site alone: a violation whose first site the
    ///         caller does not own yields nothing rather than falling through to a later one, so this
    ///         projection and the whole-solution report cannot disagree about where a violation is.
    ///     </para>
    /// </remarks>
    internal static IReadOnlyList<BuildFinding> From(CheckReport report, Func<string, bool> ownsFile)
    {
        var findings = new List<BuildFinding>();

        foreach (RuleResult result in report.Results)
        {
            if (result.Status != RuleStatus.Failed) continue;

            BuildFindingKind kind = FindingKind(result.Rule);

            foreach (Violation violation in result.Violations)
            {
                IReadOnlyList<SourceLocation> placed = violation.PlacedSites();
                List<SourceLocation> owned = placed
                    .Where(site => ownsFile(site.FilePath))
                    .ToList();
                if (owned.Count == 0) continue;

                BaselineEntry? identity = violation.BaselineIdentity();
                string message = BuildFindingMessage.Compose(result, violation);
                IEnumerable<BuildFinding> found = owned
                    .Select(site => new BuildFinding(kind, result.Rule.Id, violation.Kind, identity, site, message));
                findings.AddRange(found);
            }
        }

        return findings;
    }

    // Keyed on the posture rather than on whether the rule ratchets: a quarantine's containment rule
    // ratchets too, and a new crossing of a boundary is a law broken rather than debt someone scheduled.
    private static BuildFindingKind FindingKind(ArchRule rule)
    {
        return rule.Posture == Posture.Migrate ? BuildFindingKind.NewDebt : BuildFindingKind.BrokenRule;
    }
}
