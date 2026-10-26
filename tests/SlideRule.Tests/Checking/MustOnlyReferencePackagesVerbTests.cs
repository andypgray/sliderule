using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The package allow-list verb <c>MustOnlyReferencePackages</c> over hand-built project facts (GRAMMAR §4.10):
///     one violation per declared, non-private package that no pattern matches, each sited at that reference's own
///     declaration.
/// </summary>
/// <remarks>
///     Three decisions are pinned here beside <c>MustReferenceNoPackages</c>'s. <b>Globs ignore case</b>, because
///     NuGet compares package identifiers that way and a project may spell one in any case. <b>Private passes</b>,
///     whatever its name, exactly as on the verb this one extends. <b>A pattern matching nothing is silence</b>: a
///     project that takes none of the permitted packages is obeying the rule, so nothing warns about the pattern.
/// </remarks>
public sealed class MustOnlyReferencePackagesVerbTests
{
    // One project taking two backports, one in an unexpected case, beside a package outside the list and a private
    // analyzer; one declaring only a private build tool; and one nothing evaluated.
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Console",
            packageReferences:
            [
                ProjectFacts.Package("microsoft.bcl.asyncinterfaces", "src/Console/Console.csproj", 9),
                ProjectFacts.Package("System.Memory", "src/Console/Console.csproj", 10),
                ProjectFacts.Package("Wcwidth", "src/Console/Console.csproj", 11),
                ProjectFacts.Package("StyleCop.Analyzers", "Directory.Build.props", 6, isPrivate: true)
            ]),
        ProjectFacts.Project(
            "Contract",
            packageReferences: [ProjectFacts.Package("Microsoft.SourceLink.GitHub", "Directory.Build.props", 7, isPrivate: true)]),
        ProjectFacts.Project("Unevaluated"));

    [Fact]
    public void MustOnlyReferencePackages_PackageOutsideTheList_FailsAtItsOwnDeclaration()
    {
        // The backports match, one of them only ignoring case, and the analyzer is private: the one package the
        // rule objects to is the one on line 11.
        Checker.Verdict(Scene, arch => arch.Projects.Named("Console")
                .MustOnlyReferencePackages("Microsoft.Bcl.*", "System.Memory"))
            .ShouldHaveFailedWithProjectAtSites("Console -> Wcwidth", ["src/Console/Console.csproj:11"]);
    }

    [Fact]
    public void MustOnlyReferencePackages_EveryPackageListedOrPrivate_PassesClean()
    {
        Checker.Verdict(Scene, arch => arch.Projects.Named("Console")
                .MustOnlyReferencePackages("Microsoft.Bcl.*", "System.Memory", "Wcwidth"))
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyReferencePackages_OnlyPrivateReferences_PassesClean()
    {
        // A pattern that matches nothing the project declares is not a finding: the private build tool passes,
        // and nothing reports that no backport is referenced at all.
        Checker.Verdict(Scene, arch => arch.Projects.Named("Contract").MustOnlyReferencePackages("Microsoft.Bcl.*"))
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyReferencePackages_ProjectNothingEvaluated_PassesClean()
    {
        Checker.Verdict(Scene, arch => arch.Projects.Named("Unevaluated").MustOnlyReferencePackages("Microsoft.Bcl.*"))
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyReferencePackages_EachViolation_KeysAnEdgeFromTheProjectToItsPackage()
    {
        // The identifier is keyed as the project declares it, so a captured baseline blesses the packages it saw;
        // the report orders them ordinally, which puts the lower-case spelling last.
        RuleResult result = Checker.Verdict(Scene, arch => arch.Projects.Named("Console")
            .MustOnlyReferencePackages("System.Memory"));

        result.ShouldHaveBaselineIdentities(
        [
            BaselineEntry.ForEdge("project:Console", "package:Wcwidth"),
            BaselineEntry.ForEdge("project:Console", "package:microsoft.bcl.asyncinterfaces")
        ]);
    }

    [Fact]
    public void MustOnlyReferencePackages_PatternWithoutAWildcard_MatchesTheWholeIdentifierOnly()
    {
        // A literal pattern is an identifier, not a prefix: `System` does not permit `System.Memory`.
        Checker.Verdict(Scene, arch => arch.Projects.Named("Console")
                .MustOnlyReferencePackages("System", "Microsoft.Bcl.*", "Wcwidth"))
            .ShouldHaveFailedWithProjectAtSites("Console -> System.Memory", ["src/Console/Console.csproj:10"]);
    }
}
