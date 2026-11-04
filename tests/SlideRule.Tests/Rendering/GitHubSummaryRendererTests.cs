using System.Text;
using Shouldly;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.Checking;
using Xunit;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The Markdown job summary <see cref="GitHubSummaryRenderer.Serialize" /> writes for an in-memory report: the
///     verdict, the table of findings, the escaping a cell needs, and where the table stops under GitHub's size
///     limit. The bytes of a whole run are pinned by the <c>violated-check.summary</c> golden.
/// </summary>
public sealed class GitHubSummaryRendererTests
{
    // The budget the renderer keeps under GitHub's 1 MiB step limit.
    private const int ByteBudget = 900 * 1024;

    private static readonly string SolutionDirectory = OperatingSystem.IsWindows() ? @"C:\sol" : "/sol";

    [Fact]
    public void Serialize_RedViolation_ListsEachSiteUnderTheVerdict()
    {
        CheckReport report = Checker.Run(Sources.TwoSiteControllerModel, Sources.NoDataReference);

        string summary = Summary(report, null);

        summary.ShouldBe(
            "## SlideRule check\n\n"
            + "Checked 1 rule: 0 passed, 1 failed, 0 skipped (1 violation, 0 warnings).\n\n"
            + "| Level | Rule | Where | Finding |\n|---|---|---|---|\n"
            + "| error | layer/no-data | Test.cs:5 | App.Web.OldController references App.Data.Db |\n"
            + "| error | layer/no-data | Test.cs:6 | App.Web.OldController references App.Data.Db |\n");
    }

    [Fact]
    public void Serialize_IncompleteModel_SaysTheRunFailsAboveACleanVerdict()
    {
        // A clean report behind an exit 2 must not read as a clean run.
        var report = new CheckReport([]);
        WorkspaceDiagnostics failedLoad = WorkspaceDiagnostics.None with { FailedProjects = ["/sol/App/App.csproj"] };

        string summary = Summary(report, IncompleteModelGate.SummaryCaveat(failedLoad));

        summary.ShouldBe(
            "## SlideRule check\n\n"
            + "Checked 0 rules: 0 passed, 0 failed, 0 skipped (0 violations, 0 warnings).\n\n"
            + "The model is incomplete, so this run fails with exit 2 whatever the rules say. The job log names the "
            + "projects that did not load or whose packages did not resolve.\n\n");
    }

    [Fact]
    public void Serialize_CellMarkup_IsEscapedToLiteralText()
    {
        var report = new CheckReport(
        [
            new RuleResult(
                Checker.Rule("ref/error"), RuleStatus.Failed,
                [Violation.RuleError("a | b <T> `c` *d* [e]\r\nf & g")])
        ]);

        string summary = Summary(report, null);

        summary.ShouldEndWith(
            "| error | ref/error |  | error: a \\| b &lt;T&gt; \\`c\\` \\*d\\* \\[e\\] f &amp; g |\n");
    }

    [Fact]
    public void Serialize_PastTheBudget_StopsTheTableAndCountsWhatItLeftOut()
    {
        const int findings = 1000;
        string detail = new('x', 1000);
        List<Violation> violations = Enumerable.Range(0, findings)
            .Select(_ => Violation.RuleError(detail))
            .ToList();
        var report = new CheckReport([new RuleResult(Checker.Rule("ref/error"), RuleStatus.Failed, violations)]);

        string summary = Summary(report, null);

        int rows = summary.Split('\n')
            .Count(line => line.StartsWith("| error |", StringComparison.Ordinal));
        int omitted = findings - rows;
        int bytes = Encoding.UTF8.GetByteCount(summary);
        bytes.ShouldBeLessThanOrEqualTo(ByteBudget);
        bytes.ShouldBeGreaterThan(ByteBudget - detail.Length * 2, "the table stopped well short of the budget");
        summary.ShouldEndWith(
            $"\n{omitted} more findings are left out to keep this summary under GitHub's size limit. The check "
            + "report in the job log lists every finding.\n");
    }

    // The summary a check writes for the report: its findings, read as the check reads them, serialized.
    private static string Summary(CheckReport report, string? incompleteModel)
    {
        IReadOnlyList<GitHubFinding> findings = GitHubFinding.From(report, SolutionDirectory);
        return GitHubSummaryRenderer.Serialize(report, findings, incompleteModel);
    }
}
