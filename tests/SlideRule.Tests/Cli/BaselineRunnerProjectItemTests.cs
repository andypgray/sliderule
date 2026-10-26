using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Roslyn.Baselines;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;
using static SlideRule.Tests.TestSupport.TextNormalization;

namespace SlideRule.Tests.Cli;

/// <summary>
///     A migrated package rule through the <c>baseline</c> modes, over hand-built project facts and the real
///     file round trip (GRAMMAR §4.3, §4.10): <c>init</c> records one entry per package, a package the project
///     takes afterwards is red against the file it wrote, and <c>accept-reductions</c> splits a project-wide
///     entry from before items were keyed apart into one counted entry per package it still blesses.
/// </summary>
public sealed class BaselineRunnerProjectItemTests : IDisposable
{
    private const string RuleId = "dependencies/core-bcl-only";
    private const string BaselineFile = "core.json";

    private static readonly BaselineEntry Json = BaselineEntry.ForEdge("project:Zphil.Core", "package:Newtonsoft.Json");
    private static readonly BaselineEntry Serilog = BaselineEntry.ForEdge("project:Zphil.Core", "package:Serilog");

    private static readonly ArchitectureModel Model = Checker.Model(arch => arch.Rule(RuleId)
        .Migrate("The core still takes packages.", arch.Projects.Named("Zphil.Core").MustReferenceNoPackages())
        .Baseline(BaselineFile)
        .Because("b"));

    private readonly TempDirectory _temp = TestTempRoot.Fresh("baseline-runner-project-item");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void Init_ThenAPackageAdded_TheNewPackageIsRedAgainstTheCapturedFile()
    {
        // The defect the per-package key closes: a capture written while the project took one package must
        // not bless the next one.
        string path = _temp.PathOf(BaselineFile);
        IReadOnlyList<RuleResult> before = Check(Core("Newtonsoft.Json"), BaselineIndex.Empty).Results;
        BaselineRequests.Init(_temp.Path)
            .Apply(before, _temp.Path);
        ReadNormalizedLines(path).ShouldBe(BaselineComposer.Compose(RuleId, Json.WithSiteCount(1)));

        BaselineIndex captured = BaselineStore.LoadForModel(Model, _temp.Path);
        RuleResult result = Check(Core("Newtonsoft.Json", "Serilog"), captured).Single();

        result.ShouldHaveFailedWithProjectSubjects(["Zphil.Core -> Serilog"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void AcceptReductions_ProjectWideEntry_SplitsIntoOneCountedEntryPerPackageKeepingItsReason()
    {
        string path = WriteBaseline(BaselineComposer.Compose(
            RuleId, BaselineEntry.ForSubject("project:Zphil.Core").WithBecause("INC-7")));

        IReadOnlyList<RuleResult> results = Check(Core("Newtonsoft.Json", "Serilog"), BaselineIndex.Empty).Results;
        string echo = BaselineRequests.AcceptReductions(_temp.Path)
            .Apply(results, _temp.Path);

        echo.ShouldContain(
            "dependencies/core-bcl-only: split 1 project-wide entry into 2 entries, one per package or framework.");
        echo.ShouldNotContain("refused");
        echo.ShouldNotContain("nothing to accept");
        ReadNormalizedLines(path)
            .ShouldBe(BaselineComposer.Compose(
                RuleId,
                Json.WithSiteCount(1).WithBecause("INC-7"),
                Serilog.WithSiteCount(1).WithBecause("INC-7")));
    }

    [Fact]
    public void AcceptReductions_ProjectWideEntryBesideAnItemEntry_SplitsOnlyWhatTheItemEntryDoesNotHold()
    {
        // A package already carrying its own entry keeps it, attribution and all; the split adds only the rest.
        string path = WriteBaseline(BaselineComposer.Compose(
            RuleId,
            BaselineEntry.ForSubject("project:Zphil.Core"),
            Serilog.WithSiteCount(1).WithBecause("INC-9")));

        IReadOnlyList<RuleResult> results = Check(Core("Newtonsoft.Json", "Serilog"), BaselineIndex.Empty).Results;
        BaselineRequests.AcceptReductions(_temp.Path)
            .Apply(results, _temp.Path);

        ReadNormalizedLines(path)
            .ShouldBe(BaselineComposer.Compose(RuleId, Json.WithSiteCount(1), Serilog.WithSiteCount(1).WithBecause("INC-9")));
    }

    [Fact]
    public void AcceptReductions_ProjectWideEntryWithNoPackageLeft_IsAcceptedAsAReduction()
    {
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, BaselineEntry.ForSubject("project:Zphil.Core")));

        IReadOnlyList<RuleResult> results = Check(Core(), BaselineIndex.Empty).Results;
        string echo = BaselineRequests.AcceptReductions(_temp.Path)
            .Apply(results, _temp.Path);

        echo.ShouldContain("dependencies/core-bcl-only: accepted 1 reduction.");
        ReadNormalizedLines(path).ShouldBe(BaselineComposer.Compose(RuleId));
    }

    private static CodebaseModel Core(params string[] packages)
    {
        PackageReference[] references = packages
            .Select((name, index) => ProjectFacts.Package(name, "src/Core/Core.csproj", 9 + index))
            .ToArray();

        return ProjectFacts.Solution(ProjectFacts.Project("Zphil.Core", packageReferences: references));
    }

    private static CheckReport Check(CodebaseModel codebase, BaselineIndex baselines)
    {
        return Harness.Check(Model, codebase, baselines);
    }

    private string WriteBaseline(string content)
    {
        return _temp.WriteFile([BaselineFile], content);
    }
}
