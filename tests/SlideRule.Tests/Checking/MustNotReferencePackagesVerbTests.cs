using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The package ban verb <c>MustNotReferencePackages</c> over hand-built project facts (GRAMMAR §4.10): one
///     violation per declared package a pattern matches, private or not, each sited at that reference's own
///     declaration.
/// </summary>
/// <remarks>
///     <b>Private counts</b>: the ban is the one package verb that sees private references, because a banned
///     package is banned however it is declared, which also makes <c>MustNotReferencePackages("*")</c> the strict
///     twin of <c>MustReferenceNoPackages</c>. <b>A pattern matching nothing is silence</b>: no reference to a
///     banned package is the state the rule asks for, which is the case a ban on retired packages is in from the
///     day they retire.
/// </remarks>
public sealed class MustNotReferencePackagesVerbTests
{
    // One project taking a retired package, in its own spelling, beside a live one and a private analyzer, and one
    // project declaring nothing.
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Bus",
            packageReferences:
            [
                ProjectFacts.Package("greenpipes", "src/Bus/Bus.csproj", 9),
                ProjectFacts.Package("Microsoft.Extensions.Logging", "src/Bus/Bus.csproj", 10),
                ProjectFacts.Package("StyleCop.Analyzers", "Directory.Build.props", 6, isPrivate: true)
            ]),
        ProjectFacts.Project("Contract"));

    [Fact]
    public void MustNotReferencePackages_BannedPackage_FailsAtItsOwnDeclaration_IgnoringCase()
    {
        Checker.Run(Scene, arch => arch.Rule("packaging/no-v7-packages")
                .Enforce(arch.Projects.Matching("*").MustNotReferencePackages("GreenPipes", "MassTransit.Host"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectItemAtSites("Bus -> greenpipes", ["src/Bus/Bus.csproj:9"]);
    }

    [Fact]
    public void MustNotReferencePackages_PrivateReference_StillCounts()
    {
        // The difference from the other two package verbs: privacy exempts nothing from a ban.
        Checker.Run(Scene, arch => arch.Rule("packaging/no-stylecop")
                .Enforce(arch.Projects.Named("Bus").MustNotReferencePackages("StyleCop.*"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectItemAtSites("Bus -> StyleCop.Analyzers", ["Directory.Build.props:6"]);
    }

    [Fact]
    public void MustNotReferencePackages_PatternMatchingNothing_PassesClean()
    {
        // The retired package nobody references any more is the case the ban exists for, and it is green with no
        // warning about the pattern.
        Checker.Run(Scene, arch => arch.Rule("packaging/no-v7-packages")
                .Enforce(arch.Projects.Matching("*").MustNotReferencePackages("MassTransit.Host"))
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotReferencePackages_EveryPackage_IsTheStrictFormOfMustReferenceNoPackages()
    {
        // `*` counts every declared reference, private ones included, where MustReferenceNoPackages passes the
        // private one.
        Checker.Run(Scene, arch => arch.Rule("packaging/bare")
                .Enforce(arch.Projects.Named("Bus").MustNotReferencePackages("*"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectSubjects(
                ["Bus -> greenpipes", "Bus -> Microsoft.Extensions.Logging", "Bus -> StyleCop.Analyzers"]);
    }

    [Fact]
    public void MustNotReferencePackages_ProjectDeclaringNothing_PassesClean()
    {
        // Declared-none and never-evaluated are one state here, and both pass.
        Checker.Run(Scene, arch => arch.Rule("packaging/bare")
                .Enforce(arch.Projects.Named("Contract").MustNotReferencePackages("*"))
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotReferencePackages_EachViolation_KeysAnEdgeFromTheProjectToItsPackage()
    {
        RuleResult result = Checker.Run(Scene, arch => arch.Rule("packaging/bare")
                .Enforce(arch.Projects.Named("Bus").MustNotReferencePackages("*"))
                .Because("b"))
            .Single();

        result.Violations.Select(violation => violation.BaselineIdentity())
            .ShouldBe(
            [
                BaselineEntry.ForEdge("project:Bus", "package:Microsoft.Extensions.Logging"),
                BaselineEntry.ForEdge("project:Bus", "package:StyleCop.Analyzers"),
                BaselineEntry.ForEdge("project:Bus", "package:greenpipes")
            ]);
    }
}
