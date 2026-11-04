using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The GitHub workflow commands <see cref="GitHubAnnotationsRenderer.Serialize" /> writes for an in-memory
///     report: which findings become a line, where each one stands, and how its text is escaped. The bytes of a
///     whole run are pinned by the <c>violated-check.annotations</c> golden.
/// </summary>
public sealed class GitHubAnnotationsRendererTests
{
    // A rooted directory on either platform, so a site's relative path resolves to a known absolute one.
    private static readonly string SolutionDirectory = OperatingSystem.IsWindows() ? @"C:\sol" : "/sol";

    // The same directory as a file property carries it: forward slashes, and the drive's colon escaped.
    private static readonly string FileRoot = OperatingSystem.IsWindows() ? "C%3A/sol" : "/sol";

    // Alpha sits inside the quarantined scope and references nothing, so the tripwire's touch is the one finding.
    private static readonly CodebaseModel QuarantinedCodebase = CompilationFactory.Extract(
        "App",
        ("App.Legacy/Alpha.cs", "namespace App.Legacy { public class Alpha {} }"),
        ("App.Client/User.cs", "namespace App.Client { public class User {} }"));

    [Fact]
    public void Serialize_RedEdgeViolation_WritesAnErrorAtEachSiteWithItsRuleAsTitle()
    {
        CheckReport report = Checker.Run(Sources.TwoSiteControllerModel, Sources.NoDataReference);

        string commands = Commands(report, SolutionDirectory);

        commands.ShouldBe(
            $"::error file={FileRoot}/Test.cs,line=5,title=layer/no-data::App.Web.OldController references App.Data.Db\n"
            + $"::error file={FileRoot}/Test.cs,line=6,title=layer/no-data::App.Web.OldController references App.Data.Db\n");
    }

    [Fact]
    public void Serialize_GrandfatheredSite_WritesNothing()
    {
        BaselineIndex index = Checker.Baselines("data/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));
        CheckReport report = Checker.Run(Sources.OneControllerModel, index, Sources.NoDataAccess);
        report.Single()
            .ShouldHaveGrandfathered(1);

        string commands = Commands(report, SolutionDirectory);

        commands.ShouldBeEmpty();
    }

    [Fact]
    public void Serialize_SitelessViolations_WriteAnErrorWithNoLocation()
    {
        // SARIF has no result for these, so without a line here a red rule would show nowhere on the run.
        var report = new CheckReport(
        [
            new RuleResult(
                Checker.Rule("naming/empty"), RuleStatus.Failed,
                [Violation.EmptySubject("The subject selection matched no solution-declared types.", "a cure")]),
            new RuleResult(
                Checker.Rule("ref/error"), RuleStatus.Failed,
                [Violation.RuleError("a closed-generic backstop message")]),
            new RuleResult(
                Checker.Rule("shape/unplaced"), RuleStatus.Failed,
                [Violation.Shape(SyntheticNodes.Type("N.S"), Array.Empty<SourceLocation>())])
        ]);

        string commands = Commands(report, SolutionDirectory);

        commands.ShouldBe(
            "::error title=naming/empty::The subject selection matched no solution-declared types.\n"
            + "::error title=ref/error::error: a closed-generic backstop message\n"
            + "::error title=shape/unplaced::N.S\n");
    }

    [Fact]
    public void Serialize_TripwireTouch_WritesAWarningAtTheChangedFileUnderTheSolutionWithNoLine()
    {
        var diff = new DiffContext("/repo", ["App.Legacy/Alpha.cs"]);
        CheckReport report = Checker.Run(QuarantinedCodebase, QuarantinedScope, diff: diff);
        CheckWarning warning = report.ForRule("legacy/quarantined/tripwire")
            .Warnings.ShouldHaveSingleItem();

        string commands = Commands(report, SolutionDirectory);

        commands.ShouldBe(
            $"::warning file={FileRoot}/App.Legacy/Alpha.cs,title=legacy/quarantined/tripwire::{warning.Message}\n");
    }

    [Fact]
    public void Serialize_ReservedCharacters_AreEscapedAsTheRunnerUnescapesThem()
    {
        // The percent goes first, or the escapes added after it would be escaped again.
        string directory = OperatingSystem.IsWindows() ? @"C:\a,b%c" : "/a,b%c";
        string root = OperatingSystem.IsWindows() ? "C%3A/a%2Cb%25c" : "/a%2Cb%25c";
        CheckReport sited = Checker.Run(Sources.OneControllerModel, Sources.NoDataReference);
        var siteless = new CheckReport(
            [new RuleResult(Checker.Rule("ref/error"), RuleStatus.Failed, [Violation.RuleError("50% done\r\nnext")])]);

        string sitedCommands = Commands(sited, directory);
        string sitelessCommands = Commands(siteless, directory);

        sitedCommands.ShouldStartWith($"::error file={root}/Test.cs,line=1,");
        sitelessCommands.ShouldBe("::error title=ref/error::error: 50%25 done%0D%0Anext\n");
    }

    private static void QuarantinedScope(Arch arch)
    {
        arch.Scope("legacy/quarantined")
            .Quarantine(arch.Namespace("App.Legacy.*"))
            .Dragons("Alpha is structural.")
            .Because("Replacement scheduled; not worth stabilizing.");
    }

    // The commands a check writes for the report: its findings, read as the check reads them, serialized.
    private static string Commands(CheckReport report, string solutionDirectory)
    {
        IReadOnlyList<GitHubFinding> findings = GitHubFinding.From(report, solutionDirectory);
        return GitHubAnnotationsRenderer.Serialize(findings, solutionDirectory);
    }
}
