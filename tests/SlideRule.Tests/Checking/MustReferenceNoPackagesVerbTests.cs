using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The package-ban verb <c>MustReferenceNoPackages</c> over hand-built project facts (GRAMMAR §4.10):
///     one violation per <c>PackageReference</c> a subject project declares that is not private, each sited
///     at that reference's own declaration.
/// </summary>
/// <remarks>
///     Three decisions are pinned here. <b>Per package, per identity</b>: eight packages report eight lines
///     to delete, and each keys its own baseline entry, from the project to the package, so a package added
///     after a capture is red rather than blessed by an entry written before it existed.
///     <b>Empty passes</b>: a project that declares none and a project nothing evaluated are
///     indistinguishable by design — both carry an empty list — so both pass.
///     <b>Private passes</b>: a reference whose <c>PrivateAssets</c> is all reaches no consumer, so it is no
///     dependency. The law that forbids it too is <c>MustNotReferencePackages("*")</c>, one finding per package,
///     or a <c>Must</c> predicate over the full list, one finding per project.
/// </remarks>
public sealed class MustReferenceNoPackagesVerbTests
{
    // One project declaring two packages from two different files — its own and a central props file — and
    // one declaring none, which is also what an unevaluated project looks like.
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Domain",
            packageReferences:
            [
                ProjectFacts.Package("Newtonsoft.Json", "src/Domain/Domain.csproj", 9),
                ProjectFacts.Package("Serilog", "Directory.Packages.props", 4)
            ]),
        ProjectFacts.Project("Contract"));

    // One project declaring a shipped package beside an analyzer, and one whose only reference is a build
    // tool a props file above it declares privately.
    private static readonly CodebaseModel PrivateScene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Domain",
            packageReferences:
            [
                ProjectFacts.Package("Newtonsoft.Json", "src/Domain/Domain.csproj", 9),
                ProjectFacts.Package("StyleCop.Analyzers", "src/Domain/Domain.csproj", 10, isPrivate: true)
            ]),
        ProjectFacts.Project(
            "Contract",
            packageReferences: [ProjectFacts.Package("Microsoft.SourceLink.GitHub", "Directory.Build.props", 6, isPrivate: true)]));

    [Fact]
    public void MustReferenceNoPackages_DeclaredPackages_FailOncePerReferenceAtItsOwnSite()
    {
        RuleResult result = Checker.Run(Scene, arch => arch.Rule("packaging/domain-pure")
                .Enforce(arch.Projects.Named("Domain").MustReferenceNoPackages())
                .Because("b"))
            .Single();

        result.ShouldHaveFailedWithProjectSubjects(["Domain -> Newtonsoft.Json", "Domain -> Serilog"]);
        result.Violations.Select(violation => violation.Sites.Single()
                .ToString())
            .ShouldBe(["src/Domain/Domain.csproj:9", "Directory.Packages.props:4"], ignoreOrder: true);
    }

    [Fact]
    public void MustReferenceNoPackages_EachViolation_KeysAnEdgeFromTheProjectToItsPackage()
    {
        // This verb's identity shape: one edge per package, so a captured baseline blesses the packages it saw
        // and no others.
        RuleResult result = Checker.Run(Scene, arch => arch.Rule("packaging/domain-pure")
                .Enforce(arch.Projects.Named("Domain").MustReferenceNoPackages())
                .Because("b"))
            .Single();

        result.ShouldHaveBaselineIdentities(
        [
            BaselineEntry.ForEdge("project:Domain", "package:Newtonsoft.Json"),
            BaselineEntry.ForEdge("project:Domain", "package:Serilog")
        ]);
    }

    [Fact]
    public void MustReferenceNoPackages_NoDeclaredPackages_PassesClean()
    {
        // Declared-none and never-evaluated are one state here, and both are green: the model holds what a
        // project declares, so an empty list is the only honest reading of either.
        Checker.Run(Scene, arch => arch.Rule("packaging/contract-pure")
                .Enforce(arch.Projects.Named("Contract").MustReferenceNoPackages())
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustReferenceNoPackages_SweepsEveryProjectInTheSubject()
    {
        // The subject is a set, so the walk is per project and then per package inside it — a rule over
        // both projects reports the two packages of the one that declares any, and nothing for the other.
        Checker.Run(Scene, arch => arch.Rule("packaging/all-pure")
                .Enforce(arch.Projects.Matching("*").MustReferenceNoPackages())
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectSubjects(["Domain -> Newtonsoft.Json", "Domain -> Serilog"]);
    }

    [Fact]
    public void MustReferenceNoPackages_PrivateReference_IsNotAViolation()
    {
        // The analyzer reaches neither the project's own package nor a project referencing it, so it is
        // nobody's dependency; the shipped package declared on the line above it still reds, at that line.
        RuleResult result = Checker.Run(PrivateScene, arch => arch.Rule("packaging/domain-pure")
                .Enforce(arch.Projects.Named("Domain").MustReferenceNoPackages())
                .Because("b"))
            .Single();

        result.ShouldHaveFailedWithProjectAtSites("Domain -> Newtonsoft.Json", ["src/Domain/Domain.csproj:9"]);
    }

    [Fact]
    public void MustReferenceNoPackages_OnlyPrivateReferences_PassesClean()
    {
        Checker.Run(PrivateScene, arch => arch.Rule("packaging/contract-pure")
                .Enforce(arch.Projects.Named("Contract").MustReferenceNoPackages())
                .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void Must_NoPackageReferencesAtAll_IsTheStrictForm()
    {
        // The law that counts private references too, stated as a predicate over the full list: one finding for
        // the project the verb passes, where MustNotReferencePackages("*") would report each package.
        Checker.Run(PrivateScene, arch => arch.Rule("packaging/contract-bare")
                .Enforce(arch.Projects.Named("Contract")
                    .Must(project => project.PackageReferences.Count == 0, description: "declare no package references at all"))
                .Because("b"))
            .Single()
            .ShouldHaveFailedWithProjectSubjects(["Contract"]);
    }
}
