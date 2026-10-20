using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The framework floor verb <c>MustTarget</c> over hand-built project facts (GRAMMAR §4.10): a project reds
///     once when its framework list lacks the required moniker, at whichever declaration set that list.
/// </summary>
/// <remarks>
///     The absent-fact pass is the decision pinned here that <c>MustOnlyTarget</c> gets for free. A project
///     nothing evaluated that compiled once has an empty framework list, and the ceiling's walk finds
///     nothing in it to judge, while a floor read naively would find its framework missing from it and red
///     every such project.
///     Unknown passes, so the floor tests for the empty list rather than walking it.
/// </remarks>
public sealed class MustTargetVerbTests
{
    // One project per fact shape: a single framework sited in its own file, two frameworks sited in a props
    // file above it (the shared-policy case), and a project nothing evaluated at all.
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Contract", ["netstandard2.0"], ProjectFacts.Site("src/Contract/Contract.csproj", 5)),
        ProjectFacts.Project(
            "Host", ["net48", "net8.0"], ProjectFacts.Site("Directory.Build.props", 3)),
        ProjectFacts.Project("Unevaluated"));

    [Fact]
    public void MustTarget_FrameworkMissing_FailsAtTheDeclaringPropsFile_AndIsFlaggedAsMissing()
    {
        // The site is where the framework list is declared, here a props file above the project: that is the
        // line a fix adds the missing moniker to.
        // The flag is what the wording reads to say "does not target" rather than "targets", over the same
        // framework slot MustOnlyTarget fills with a framework the project does target.
        Violation violation = Checker.Run(Scene, arch => arch.Rule("packaging/standard-floor")
                .Enforce(arch.Projects.Named("Host").MustTarget("netstandard2.0"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectAtSites("Host -> netstandard2.0", ["Directory.Build.props:3"]);

        violation.FrameworkMissing.ShouldBeTrue();
    }

    [Fact]
    public void MustTarget_FrameworkAmongSeveral_PassesClean()
    {
        // A floor, not a ceiling: the project targets other frameworks beside the required one and passes.
        Checker.Run(Scene, arch => arch.Rule("packaging/modern-floor")
                .Enforce(arch.Projects.Named("Host").MustTarget("net8.0"))
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustTarget_ProjectNothingEvaluated_PassesClean()
    {
        // The absent-fact row: an empty framework list is what nothing evaluated looks like, and unknown never
        // reds.
        Checker.Run(Scene, arch => arch.Rule("packaging/standard-floor")
                .Enforce(arch.Projects.Named("Unevaluated").MustTarget("netstandard2.0"))
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustTarget_FrameworksWithNoSite_FailsUnlocated()
    {
        // Frameworks read from what the load told apart rather than from an evaluation carry no site. The
        // missing framework is still a real finding, so it reds with nothing to point at.
        CodebaseModel siteless = ProjectFacts.Solution(ProjectFacts.Project("Legacy", ["net48"]));

        Checker.Run(siteless, arch => arch.Rule("packaging/modern-floor")
                .Enforce(arch.Projects.Named("Legacy").MustTarget("net8.0"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectAtSites("Legacy -> net8.0", []);
    }

    [Fact]
    public void MustTarget_ComparesTheMonikerOrdinally()
    {
        // Monikers are compared as the normalized short forms are spelled, so a differently cased moniker is a
        // different framework, as it is for MustOnlyTarget.
        Checker.Run(Scene, arch => arch.Rule("packaging/standard-floor")
                .Enforce(arch.Projects.Named("Contract").MustTarget("NetStandard2.0"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectAtSites("Contract -> NetStandard2.0", ["src/Contract/Contract.csproj:5"]);
    }

    [Fact]
    public void MustTarget_OneViolationPerProjectLackingIt()
    {
        // Each project lacking the framework is one finding keyed by the moniker, so a migrated rule
        // grandfathers the projects it saw and reds on the next one (GRAMMAR §4.3, §4.10).
        Checker.Run(Scene, arch => arch.Rule("packaging/modern-floor")
                .Enforce(arch.Projects.Matching("*").MustTarget("net10.0"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectSubjects(["Contract -> net10.0", "Host -> net10.0"]);
    }
}
