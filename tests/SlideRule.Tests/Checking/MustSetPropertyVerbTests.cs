using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The property verb <c>MustSetProperty</c> over hand-built project evaluations (GRAMMAR §4.10): one violation
///     per evaluation of a project in which nothing in the solution set the property, or set it to another value.
/// </summary>
/// <remarks>
///     Three decisions are pinned here. <b>Per evaluation</b>: a multi-targeted project is judged once per
///     framework, because a <c>$(TargetFramework)</c> condition can set a property for one framework only, and
///     the framework is part of each violation's identity. <b>Set means declared</b>: the evaluations carry only
///     what a declaration in the solution set, so a value the SDK defaults is an unset property here.
///     <b>MSBuild's comparisons</b>: the name is matched ignoring case, and the value ignoring case and
///     surrounding whitespace.
/// </remarks>
public sealed class MustSetPropertyVerbTests
{
    // A multi-targeted project that enables nullable analysis for one framework only, a single-targeted one that
    // disables it in a props file above it, one that enables it with its own spelling, and one nothing evaluated.
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Abstractions",
            ["net8.0", "netstandard2.0"],
            evaluations:
            [
                ProjectFacts.Evaluation(
                    "net8.0", "src/Abstractions/Abstractions.csproj", ("Nullable", "enable", "src/Abstractions/Abstractions.csproj", 6)),
                ProjectFacts.Evaluation("netstandard2.0", "src/Abstractions/Abstractions.csproj")
            ]),
        ProjectFacts.Project(
            "Legacy",
            ["net48"],
            evaluations:
            [
                ProjectFacts.Evaluation("net48", "src/Legacy/Legacy.csproj", ("Nullable", "disable", "Directory.Build.props", 4))
            ]),
        ProjectFacts.Project(
            "Modern",
            ["net8.0"],
            evaluations:
            [
                ProjectFacts.Evaluation("net8.0", "src/Modern/Modern.csproj", ("nullable", " Enable ", "src/Modern/Modern.csproj", 5))
            ]),
        ProjectFacts.Project("Unevaluated"));

    [Fact]
    public void MustSetProperty_UnsetForOneFramework_FailsForThatFrameworkAtTheProjectFile()
    {
        // The framework that enables it passes; the one that sets nothing reds, sited where the missing
        // declaration would go.
        Violation violation = Checker.Verdict(Scene, arch => arch.Projects.Named("Abstractions")
                .MustSetProperty("Nullable", "enable"))
            .ShouldHaveFailedWithProjectAtSites(
                "Abstractions -> Nullable@netstandard2.0", ["src/Abstractions/Abstractions.csproj:1"]);

        violation.PropertyValue.ShouldBeNull();
    }

    [Fact]
    public void MustSetProperty_SetToAnotherValue_FailsAtTheDefinitionThatWon()
    {
        Violation violation = Checker.Verdict(Scene, arch => arch.Projects.Named("Legacy")
                .MustSetProperty("Nullable", "enable"))
            .ShouldHaveFailedWithProjectAtSites("Legacy -> Nullable@net48", ["Directory.Build.props:4"]);

        violation.PropertyValue.ShouldBe("disable");
    }

    [Fact]
    public void MustSetProperty_NameAndValueInAnotherCase_PassClean()
    {
        // MSBuild matches property names ignoring case and compares condition strings the same way, and the value
        // here carries the whitespace a property written across lines would.
        Checker.Verdict(Scene, arch => arch.Projects.Named("Modern").MustSetProperty("Nullable", "enable"))
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustSetProperty_ProjectNothingEvaluated_PassesClean()
    {
        // No evaluation, nothing to judge: unknown never reds.
        Checker.Verdict(Scene, arch => arch.Projects.Named("Unevaluated").MustSetProperty("Nullable", "enable"))
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustSetProperty_EachViolation_KeysAnEdgeFromTheProjectToThePropertyAtItsFramework()
    {
        // Two frameworks that both lack the property share the project-file site, so the framework rides in the
        // identity: without it a migrated rule would pass the next framework that loses the property.
        CodebaseModel scene = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Bus",
                ["net472", "net8.0"],
                evaluations:
                [
                    ProjectFacts.Evaluation("net472", "src/Bus/Bus.csproj"),
                    ProjectFacts.Evaluation("net8.0", "src/Bus/Bus.csproj")
                ]),
            ProjectFacts.Project("Tool", evaluations: [ProjectFacts.Evaluation(null, "src/Tool/Tool.csproj")]));

        RuleResult result = Checker.Verdict(scene, arch => arch.Projects.Matching("*")
            .MustSetProperty("Nullable", "enable"));

        result.ShouldHaveBaselineIdentities(
        [
            BaselineEntry.ForEdge("project:Bus", "property:Nullable@net472"),
            BaselineEntry.ForEdge("project:Bus", "property:Nullable@net8.0"),
            BaselineEntry.ForEdge("project:Tool", "property:Nullable")
        ]);
    }
}
