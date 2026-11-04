using System.Text;
using SlideRule.Checking;
using SlideRule.Internal;
using SlideRule.Rendering;
using SlideRule.Roslyn.Caching;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as a Markdown job summary for GitHub Actions: the check's verdict, then
///     one table row per <see cref="GitHubFinding" />, so a finding GitHub's annotation limit hides is still listed.
/// </summary>
internal static class GitHubSummaryRenderer
{
    // GitHub drops a step's whole summary past 1 MiB, so the table stops short of that, footer included.
    private const int ByteBudget = 900 * 1024;

    private const string Heading = "## SlideRule check";
    private const string TableHeader = "| Level | Rule | Where | Finding |\n|---|---|---|---|\n";

    /// <summary>
    ///     Writes the summary for <paramref name="report" /> and its <paramref name="findings" /> to
    ///     <paramref name="path" />, atomically and creating the directory. <paramref name="incompleteModel" /> is
    ///     the <see cref="IncompleteModelGate.SummaryCaveat" /> when the incomplete-model gate will exit 2, which
    ///     the summary then states above the findings, and null otherwise. Throws on a bad path or an I/O failure.
    /// </summary>
    public static void Render(
        string path, CheckReport report, IReadOnlyList<GitHubFinding> findings, string? incompleteModel)
    {
        AtomicFile.WriteAllText(path, Serialize(report, findings, incompleteModel));
    }

    /// <summary>
    ///     The summary for <paramref name="report" /> and its <paramref name="findings" />, with no filesystem behind
    ///     it.
    /// </summary>
    internal static string Serialize(CheckReport report, IReadOnlyList<GitHubFinding> findings, string? incompleteModel)
    {
        var text = new StringBuilder();
        text.Append(Heading)
            .Append("\n\n")
            .Append(HumanReportRenderer.SummaryLine(report))
            .Append("\n\n");
        if (incompleteModel is not null)
            text.Append(incompleteModel)
                .Append("\n\n");

        if (findings.Count == 0) return text.ToString();

        text.Append(TableHeader);
        int bytes = Encoding.UTF8.GetByteCount(text.ToString());
        var listed = 0;
        foreach (GitHubFinding finding in findings)
        {
            string row = Row(finding);
            int rowBytes = Encoding.UTF8.GetByteCount(row);
            if (bytes + rowBytes + Encoding.UTF8.GetByteCount(Footer(findings.Count - listed)) > ByteBudget) break;

            text.Append(row);
            bytes += rowBytes;
            listed++;
        }

        if (listed < findings.Count) text.Append(Footer(findings.Count - listed));

        return text.ToString();
    }

    private static string Footer(int omitted)
    {
        return $"\n{Plurals.Counted(omitted, "more finding")} {Plurals.Verb(omitted)} left out to keep this summary "
               + "under GitHub's size limit. The check report in the job log lists every finding.\n";
    }

    private static string Row(GitHubFinding finding)
    {
        string where = finding.Path ?? string.Empty;
        if (finding.Line is { } line) where += $":{line}";

        return $"| {finding.Level} | {Cell(finding.RuleId)} | {Cell(where)} | {Cell(finding.Message)} |\n";
    }

    // A table cell holds one line of literal text: a pipe would end the cell, and markup or a generic type's
    // angle brackets would render as something other than what the check said.
    private static string Cell(string value)
    {
        var cell = new StringBuilder(value.Length);
        foreach (char c in value)
            switch (c)
            {
                case '\r':
                    break;
                case '\n':
                    cell.Append(' ');
                    break;
                case '&':
                    cell.Append("&amp;");
                    break;
                case '<':
                    cell.Append("&lt;");
                    break;
                case '>':
                    cell.Append("&gt;");
                    break;
                case '|' or '\\' or '`' or '*' or '_' or '~' or '[' or ']':
                    cell.Append('\\')
                        .Append(c);
                    break;
                default:
                    cell.Append(c);
                    break;
            }

        return cell.ToString();
    }
}
