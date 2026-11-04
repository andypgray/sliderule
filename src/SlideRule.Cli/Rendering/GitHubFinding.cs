using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Rendering;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     One finding as the GitHub renderers write it: its level, its rule, and where it stands, which is a
///     solution-relative forward-slashed path and a line, a path alone, or nowhere.
/// </summary>
/// <remarks>
///     Grandfathered sites are not findings here: GitHub shows ten error and ten warning annotations a step, and
///     baselined debt would spend them.
/// </remarks>
internal sealed record GitHubFinding(string Level, string RuleId, string? Path, int? Line, string Message)
{
    internal const string ErrorLevel = "error";
    internal const string WarningLevel = "warning";

    /// <summary>
    ///     Each red violation at the sites the human report places it, or with no location when it has none,
    ///     then each check warning, rule by rule in model order.
    /// </summary>
    internal static IReadOnlyList<GitHubFinding> From(CheckReport report, string solutionDirectory)
    {
        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        var findings = new List<GitHubFinding>();
        foreach (RuleResult result in report.Results)
        {
            string ruleId = result.Rule.Id;
            foreach (Violation violation in result.Violations)
            {
                IReadOnlyList<SourceLocation> placed = violation.PlacedSites();
                if (placed.Count > 0)
                {
                    string text = violation.Text();
                    IEnumerable<GitHubFinding> sites = placed.Select(site => new GitHubFinding(
                        ErrorLevel, ruleId, Relative(relativizer, solutionDirectory, site.FilePath), site.Line, text));
                    findings.AddRange(sites);
                }
                else if (violation.UnplacedText() is { } unplaced)
                {
                    findings.Add(new GitHubFinding(ErrorLevel, ruleId, null, null, unplaced));
                }
            }

            foreach (CheckWarning warning in result.Warnings)
                findings.Add(new GitHubFinding(WarningLevel, ruleId, warning.File, null, warning.Message));
        }

        return findings;
    }

    // A site's path is absolute as the extractor records it; one written relative is read against the solution.
    private static string Relative(PathFormat.Relativizer relativizer, string solutionDirectory, string path)
    {
        return relativizer.Relative(System.IO.Path.Combine(solutionDirectory, path));
    }
}
