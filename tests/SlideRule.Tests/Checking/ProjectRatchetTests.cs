using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The ratchet over a project subject (GRAMMAR §4.3, §4.10): a Migrate rule partitions its project
///     violations against the captured baseline, so blessed packaging debt passes and a project nobody
///     blessed stays red.
/// </summary>
/// <remarks>
///     The rows that matter most are the per-item ones. The package verbs mint one violation per declared
///     package they object to, <c>MustOnlyTarget</c> one per framework it does not permit, <c>MustTarget</c>
///     one per project lacking the framework it requires and <c>MustSetProperty</c> one per framework build
///     the property is missing from. Each keys an edge from the project to its item, so a package or framework
///     the project takes after the capture is red, and so is a project that drops the required framework or a
///     build that loses the property. A project-wide entry written before the items were keyed apart still
///     grandfathers every item of its project, as an uncounted entry, until accept-reductions splits it.
/// </remarks>
public sealed class ProjectRatchetTests
{
    private static readonly CodebaseModel Scene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Zphil.Contract", isPackable: true, isPackableSite: ProjectFacts.Site("src/Contract/Contract.csproj", 4)),
        ProjectFacts.Project(
            "Zphil.Cli", isPackable: true, isPackableSite: ProjectFacts.Site("src/Cli/Cli.csproj", 4)));

    private static readonly CodebaseModel PackageScene = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Zphil.Domain",
            packageReferences:
            [
                ProjectFacts.Package("Newtonsoft.Json", "src/Domain/Domain.csproj", 9),
                ProjectFacts.Package("Serilog", "src/Domain/Domain.csproj", 10)
            ]),
        ProjectFacts.Project(
            "Zphil.Web", packageReferences: [ProjectFacts.Package("Serilog", "src/Web/Web.csproj", 7)]));

    private static readonly CodebaseModel FloorScene = ProjectFacts.Solution(
        Targeting("Zphil.Domain", "net48"),
        Targeting("Zphil.Web", "net48"));

    [Fact]
    public void Migrate_BaselinedProject_Grandfathers_WhileTheOtherStaysRed()
    {
        BaselineIndex index = Checker.Baselines(
            "packaging/nothing-ships", BaselineEntry.ForSubject("project:Zphil.Contract"));

        RuleResult result = Checker.Run(Scene, index, arch => arch.Rule("packaging/nothing-ships")
                .Migrate(
                    "Two projects still publish packages.",
                    arch.Projects.Matching("Zphil.*").MustNotBePackable())
                .Because("A package nobody meant to publish is an API nobody meant to promise."))
            .Single();

        result.ShouldHaveFailedWithProjectSubjects(["Zphil.Cli"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void Migrate_ProjectIdentity_IsTheProjectTaggedName()
    {
        // The pinned identity form. A project carries no DocumentationCommentId, so the tag is the word `project`.
        RuleResult result = Checker.Run(Scene, BaselineIndex.Empty, arch => arch.Rule("packaging/nothing-ships")
                .Migrate(
                    "Two projects still publish packages.",
                    arch.Projects.Matching("Zphil.*").MustNotBePackable())
                .Because("b"))
            .Single();

        result.Violations.Select(violation => violation.BaselineIdentity())
            .ShouldBe(
            [
                BaselineEntry.ForSubject("project:Zphil.Cli"),
                BaselineEntry.ForSubject("project:Zphil.Contract")
            ]);
    }

    [Fact]
    public void Migrate_PackageIdentity_IsAnEdgeFromTheProjectToThePackage()
    {
        // The pinned per-item form: the project's identity, then the package tagged the way the project is.
        RuleResult result = Checker.Run(PackageScene, BaselineIndex.Empty, arch => arch.Rule("packaging/domain-pure")
                .Migrate("Packages everywhere.", arch.Projects.Matching("Zphil.*").MustReferenceNoPackages())
                .Because("b"))
            .Single();

        result.Violations.Select(violation => violation.BaselineIdentity())
            .ShouldBe(
            [
                BaselineEntry.ForEdge("project:Zphil.Domain", "package:Newtonsoft.Json"),
                BaselineEntry.ForEdge("project:Zphil.Domain", "package:Serilog"),
                BaselineEntry.ForEdge("project:Zphil.Web", "package:Serilog")
            ]);
    }

    [Fact]
    public void Migrate_FrameworkIdentity_IsAnEdgeFromTheProjectToTheFramework()
    {
        CodebaseModel scene = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Zphil.Domain",
                targetFrameworks: ["net48", "net6.0", "net8.0"],
                targetFrameworksSite: ProjectFacts.Site("src/Domain/Domain.csproj", 3)));

        RuleResult result = Checker.Run(scene, BaselineIndex.Empty, arch => arch.Rule("packaging/supported")
                .Migrate("Old frameworks linger.", arch.Projects.Matching("Zphil.*").MustOnlyTarget("net8.0"))
                .Because("b"))
            .Single();

        result.Violations.Select(violation => violation.BaselineIdentity())
            .ShouldBe(
            [
                BaselineEntry.ForEdge("project:Zphil.Domain", "framework:net48"),
                BaselineEntry.ForEdge("project:Zphil.Domain", "framework:net6.0")
            ]);
    }

    [Fact]
    public void Migrate_MissingFrameworkIdentity_IsAnEdgeFromTheProjectToTheFramework()
    {
        // The floor keys what the ceiling keys: the framework stands in the target slot, here the one the
        // project lacks rather than one it targets.
        RuleResult result = Checker.Run(FloorScene, BaselineIndex.Empty, FloorRule).Single();

        result.Violations.Select(violation => violation.BaselineIdentity())
            .ShouldBe(
            [
                BaselineEntry.ForEdge("project:Zphil.Domain", "framework:net8.0"),
                BaselineEntry.ForEdge("project:Zphil.Web", "framework:net8.0")
            ]);
    }

    [Fact]
    public void Migrate_ProjectDroppingTheRequiredFrameworkAfterCapture_IsRed()
    {
        // Capture one project lacking the floor; a second project that drops it later is red while the first
        // stays grandfathered.
        CodebaseModel captured = ProjectFacts.Solution(
            Targeting("Zphil.Domain", "net48"),
            Targeting("Zphil.Web", "net48", "net8.0"));
        BaselineIndex index = Capture(captured, FloorRule);

        RuleResult result = Checker.Run(FloorScene, index, FloorRule).Single();

        result.ShouldHaveFailedWithProjectSubjects(["Zphil.Web -> net8.0"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void Migrate_PackageAddedAfterCapture_IsRed()
    {
        // The defect this key exists to close: capture a project's one package, add a second, and the second is
        // red while the first stays grandfathered.
        CodebaseModel captured = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Zphil.Domain", packageReferences: [ProjectFacts.Package("Newtonsoft.Json", "src/Domain/Domain.csproj", 9)]));
        BaselineIndex index = Capture(captured, PackageRule);

        RuleResult result = Checker.Run(PackageScene, index, PackageRule).Single();

        result.ShouldHaveFailedWithProjectSubjects(["Zphil.Domain -> Serilog", "Zphil.Web -> Serilog"]);
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveUncounted(0);
    }

    [Fact]
    public void Migrate_PackageOutsideTheAllowListAddedAfterCapture_IsRed()
    {
        // The allow-list keys what MustReferenceNoPackages keys, so the package a project takes after the capture
        // is a new identity and red, while the one captured stays grandfathered.
        CodebaseModel captured = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Zphil.Domain", packageReferences: [ProjectFacts.Package("Newtonsoft.Json", "src/Domain/Domain.csproj", 9)]));
        BaselineIndex index = Capture(captured, AllowListRule);

        RuleResult result = Checker.Run(PackageScene, index, AllowListRule).Single();

        result.ShouldHaveFailedWithProjectSubjects(["Zphil.Domain -> Serilog", "Zphil.Web -> Serilog"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void Migrate_BannedPackageAddedAfterCapture_IsRed()
    {
        CodebaseModel captured = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Zphil.Domain", packageReferences: [ProjectFacts.Package("Serilog", "src/Domain/Domain.csproj", 10)]));
        BaselineIndex index = Capture(captured, BanRule);

        RuleResult result = Checker.Run(PackageScene, index, BanRule).Single();

        result.ShouldHaveFailedWithProjectSubjects(["Zphil.Web -> Serilog"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void Migrate_FrameworkLosingThePropertyAfterCapture_IsRed()
    {
        // The capture blesses the one framework that lacked the property; the second framework losing it later
        // shares that framework's project-file site and is still a new identity, so it reds.
        CodebaseModel captured = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Zphil.Domain",
                evaluations:
                [
                    ProjectFacts.Evaluation("net48", "src/Domain/Domain.csproj"),
                    ProjectFacts.Evaluation("net8.0", "src/Domain/Domain.csproj", ("Nullable", "enable", "src/Domain/Domain.csproj", 5))
                ]));
        CodebaseModel later = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Zphil.Domain",
                evaluations:
                [
                    ProjectFacts.Evaluation("net48", "src/Domain/Domain.csproj"),
                    ProjectFacts.Evaluation("net8.0", "src/Domain/Domain.csproj")
                ]));
        BaselineIndex index = Capture(captured, PropertyRule);

        RuleResult result = Checker.Run(later, index, PropertyRule).Single();

        result.ShouldHaveFailedWithProjectSubjects(["Zphil.Domain -> Nullable@net8.0"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void Migrate_FrameworkAddedAfterCapture_IsRed()
    {
        CodebaseModel captured = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Zphil.Domain",
                targetFrameworks: ["net48", "net8.0"],
                targetFrameworksSite: ProjectFacts.Site("src/Domain/Domain.csproj", 3)));
        CodebaseModel later = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Zphil.Domain",
                targetFrameworks: ["net472", "net48", "net8.0"],
                targetFrameworksSite: ProjectFacts.Site("src/Domain/Domain.csproj", 3)));
        BaselineIndex index = Capture(captured, FrameworkRule);

        RuleResult result = Checker.Run(later, index, FrameworkRule).Single();

        result.ShouldHaveFailedWithProjectSubjects(["Zphil.Domain -> net472"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void Migrate_ProjectWideEntry_GrandfathersEveryItemAsOneUncountedEntry()
    {
        // A file captured before items were keyed apart holds one subject entry for the project. It still
        // blesses every package that project declares, reads as uncounted so status offers the split, and is
        // not stale; the project nobody blessed still reports each of its own.
        BaselineIndex index = Checker.Baselines(
            "packaging/domain-pure", BaselineEntry.ForSubject("project:Zphil.Domain"));

        RuleResult result = Checker.Run(PackageScene, index, PackageRule).Single();

        result.ShouldHaveFailedWithProjectSubjects(["Zphil.Web -> Serilog"]);
        result.ShouldHaveGrandfathered(2);
        result.ShouldHaveUncounted(1);
        result.ShouldHaveStale(0);
    }

    [Fact]
    public void Migrate_ItemEntryBesideAProjectWideEntry_MatchesExactly()
    {
        // An item that carries an entry of its own matches that entry, with its count, rather than the
        // project-wide one standing beside it; the other item still falls back.
        BaselineIndex index = Checker.Baselines(
            "packaging/domain-pure",
            BaselineEntry.ForSubject("project:Zphil.Domain"),
            BaselineEntry.ForEdge("project:Zphil.Domain", "package:Serilog").WithSiteCount(1));

        RuleResult result = Checker.Run(PackageScene, index, PackageRule).Single();

        result.GrandfatheredEntries.ShouldBe(
        [
            BaselineEntry.ForSubject("project:Zphil.Domain"),
            BaselineEntry.ForEdge("project:Zphil.Domain", "package:Serilog")
        ]);
        result.ShouldHaveUncounted(1);
        result.ShouldHaveStale(0);
    }

    [Fact]
    public void Migrate_ViolationOrder_IsProjectThenPackage()
    {
        // Deterministic report order across the project and package slots: the project name is the primary
        // key and the package name stands where a Target would, so one project's packages sort among themselves.
        RuleResult result = Checker.Run(PackageScene, BaselineIndex.Empty, arch => arch.Rule("packaging/domain-pure")
                .Migrate("Packages everywhere.", arch.Projects.Matching("Zphil.*").MustReferenceNoPackages())
                .Because("b"))
            .Single();

        result.Violations.Select(violation => $"{violation.SubjectProject!.Name} -> {violation.Package!.Name}")
            .ShouldBe(["Zphil.Domain -> Newtonsoft.Json", "Zphil.Domain -> Serilog", "Zphil.Web -> Serilog"]);
    }

    [Fact]
    public void Migrate_ProjectWhosePackagesAreAllPrivate_LeavesItsEntryStale()
    {
        // An entry blessed this project's package debt, and every package it still declares is private, so
        // nothing counts against the rule any more and the entry reads stale rather than grandfathering nothing.
        CodebaseModel scene = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Zphil.Domain",
                packageReferences: [ProjectFacts.Package("StyleCop.Analyzers", "src/Domain/Domain.csproj", 9, isPrivate: true)]));
        BaselineIndex index = Checker.Baselines(
            "packaging/domain-pure", BaselineEntry.ForEdge("project:Zphil.Domain", "package:StyleCop.Analyzers"));

        RuleResult result = Checker.Run(scene, index, PackageRule).Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(0);
        result.ShouldHaveStale(1);
    }

    private static void PackageRule(Arch arch)
    {
        arch.Rule("packaging/domain-pure")
            .Migrate("Projects still take packages directly.", arch.Projects.Matching("Zphil.*").MustReferenceNoPackages())
            .Because("b");
    }

    private static void FloorRule(Arch arch)
    {
        arch.Rule("packaging/floor")
            .Migrate("Two projects still target only net48.", arch.Projects.Matching("Zphil.*").MustTarget("net8.0"))
            .Because("b");
    }

    // A project whose frameworks were evaluated, declared on its own file's third line.
    private static ProjectNode Targeting(string name, params string[] frameworks)
    {
        return ProjectFacts.Project(
            name, targetFrameworks: frameworks, targetFrameworksSite: ProjectFacts.Site($"src/{name}/{name}.csproj", 3));
    }

    private static void AllowListRule(Arch arch)
    {
        arch.Rule("packaging/no-new-packages")
            .Migrate("Projects take packages nobody reviewed.", arch.Projects.Matching("Zphil.*").MustOnlyReferencePackages("Microsoft.*"))
            .Because("b");
    }

    private static void BanRule(Arch arch)
    {
        arch.Rule("packaging/one-logger")
            .Migrate("Serilog lingers beside Microsoft.Extensions.Logging.", arch.Projects.Matching("Zphil.*").MustNotReferencePackages("Serilog*"))
            .Because("b");
    }

    private static void PropertyRule(Arch arch)
    {
        arch.Rule("build/nullable")
            .Migrate("Older frameworks build without nullable analysis.", arch.Projects.Matching("Zphil.*").MustSetProperty("Nullable", "enable"))
            .Because("b");
    }

    private static void FrameworkRule(Arch arch)
    {
        arch.Rule("packaging/supported")
            .Migrate("Old frameworks linger.", arch.Projects.Matching("Zphil.*").MustOnlyTarget("net8.0"))
            .Because("b");
    }

    // What a capture records, read off a first run with no baseline: each violation's identity with the sites it
    // was seen at, which is the shape baseline init writes.
    private static BaselineIndex Capture(CodebaseModel scene, Action<Arch> spec)
    {
        RuleResult first = Checker.Run(scene, BaselineIndex.Empty, spec).Single();
        BaselineEntry[] entries = first.Violations
            .Select(violation => violation.BaselineIdentity()!.WithSiteCount(violation.Sites.Count))
            .ToArray();

        return Checker.Baselines(first.Rule.Id, entries);
    }
}
