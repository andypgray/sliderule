using SlideRule.Checking;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     A rule's verdict as text, for comparing one run's rules with another's: its status, each violation with
///     its sites, and each warning.
/// </summary>
/// <remarks>
///     It leaves out the subject coverage. A type added to a project moves the swept count of every rule over
///     that project, and a count is scope rather than a finding, so two runs that differ by an added type would
///     otherwise disagree on rules the type never touched.
/// </remarks>
internal static class RuleVerdict
{
    /// <summary>The verdict of <paramref name="result" />, one line per finding under its status.</summary>
    internal static string Of(RuleResult result)
    {
        IEnumerable<string> violations = result.Violations.Select(static violation =>
            $"{violation.Kind} {violation.OrderKey} "
            + string.Join(", ", violation.Sites.Select(static site => $"{site.FilePath}:{site.Line}")));
        IEnumerable<string> warnings = result.Warnings.Select(static warning => $"{warning.Kind} {warning.Message}");

        return string.Join("\n", violations.Concat(warnings)
            .Prepend(result.Status.ToString()));
    }
}
