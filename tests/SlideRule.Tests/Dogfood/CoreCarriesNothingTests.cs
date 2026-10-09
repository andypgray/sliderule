using Xunit;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Checking;

namespace SlideRule.Tests.Dogfood;

/// <summary>
///     The self-spec's <c>packaging/core-carries-nothing</c> rule itself, rather than a restatement of it, over
///     hand-built facts for Core: a package Core would impose on every spec project still reds, and a private
///     reference beside it, the shape an analyzer or a build tool takes, does not. No workspace loads here; the
///     self-check over the real solution is <see cref="SelfSpecTests" />'s.
/// </summary>
public sealed class CoreCarriesNothingTests
{
    private const string RuleId = "packaging/core-carries-nothing";
    private const string CoreProjectFile = "src/SlideRule/SlideRule.csproj";

    [Fact]
    public void CoreCarriesNothing_AShippedReferenceBesideAPrivateOne_RedsOnTheShippedOneAtItsSite()
    {
        // Arrange
        CodebaseModel scene = ProjectFacts.Solution(
            ProjectFacts.Project(
                "SlideRule",
                packageReferences:
                [
                    ProjectFacts.Package("Microsoft.CodeAnalysis.PublicApiAnalyzers", CoreProjectFile, 31, isPrivate: true),
                    ProjectFacts.Package("System.Text.Json", CoreProjectFile, 32)
                ]));

        // Act
        RuleResult result = ArchChecker.Check(SelfSpecTests.SelfModel, scene)
            .ForRule(RuleId);

        // Assert — the dependency every consumer would inherit, at the line to delete, and nothing for the
        // analyzer, which ships to no one.
        result.ShouldHaveFailedWithPackageAtSites(
            "SlideRule -> System.Text.Json", [$"{CoreProjectFile}:32"]);
    }

    [Fact]
    public void CoreCarriesNothing_APrivateReferenceAlone_Passes()
    {
        // Arrange
        CodebaseModel scene = ProjectFacts.Solution(
            ProjectFacts.Project(
                "SlideRule",
                packageReferences:
                [
                    ProjectFacts.Package("Microsoft.CodeAnalysis.PublicApiAnalyzers", CoreProjectFile, 31, isPrivate: true)
                ]));

        // Act
        RuleResult result = ArchChecker.Check(SelfSpecTests.SelfModel, scene)
            .ForRule(RuleId);

        // Assert
        result.ShouldHavePassedClean();
    }
}
