using System.Text.Json;
using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     How a project violation reads on the three report surfaces (GRAMMAR §4.10): the human block, the
///     <c>--json</c> document, and SARIF. Four texts to pin per surface — a packaging violation, which
///     names the project alone, a per-package one, which names the package it declares, a per-framework
///     one, which names the framework it targets, and a missing-framework one, which names the framework it
///     does not target.
/// </summary>
/// <remarks>
///     The three surfaces take a violation's wording from <c>ViolationWording</c>, and each is still asserted
///     on its own: the pin is that every surface reaches the owner, so a surface that grows a twin of its own
///     reds here rather than in a consumer's scanner. What is asserted about JSON is also the slot discipline:
///     <c>subjectProject</c>, <c>package</c> and <c>framework</c> are additive and null-omitted, so a report
///     from a spec with no project rule carries none of them.
/// </remarks>
public sealed class ProjectViolationRenderTests
{
    private static readonly CodebaseModel Packable = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Zphil.Internal", isPackable: true, isPackableSite: ProjectFacts.Site("Internal.csproj", 4)));

    private static readonly CodebaseModel Packaged = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Zphil.Domain", packageReferences: [ProjectFacts.Package("Serilog", "Domain.csproj", 9)]));

    private static readonly CodebaseModel Analyzed = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Zphil.Domain",
            packageReferences: [ProjectFacts.Package("StyleCop.Analyzers", "Directory.Build.props", 6, isPrivate: true)]));

    private static readonly CodebaseModel Configured = ProjectFacts.Solution(
        ProjectFacts.Project(
            "Zphil.Bus",
            ["net472", "net8.0"],
            evaluations:
            [
                ProjectFacts.Evaluation("net472", "Bus.csproj"),
                ProjectFacts.Evaluation("net8.0", "Bus.csproj", ("Nullable", "disable", "Directory.Build.props", 7))
            ]));

    private static readonly CodebaseModel Targeted = ProjectFacts.Solution(
        ProjectFacts.Project("Zphil.Host", ["net48", "net8.0"], ProjectFacts.Site("Directory.Build.props", 3)));

    [Fact]
    public void HumanBlock_PackagingViolation_NamesTheProjectAtItsDeclaringSite()
    {
        PackableReport()
            .Single()
            .HumanBlock()
            .ShouldContain("Internal.csproj:4 — Zphil.Internal");
    }

    [Fact]
    public void HumanBlock_PerPackageViolation_NamesTheProjectAndThePackage()
    {
        PackagedReport()
            .Single()
            .HumanBlock()
            .ShouldContain("Domain.csproj:9 — Zphil.Domain references package Serilog");
    }

    [Fact]
    public void HumanBlock_PerFrameworkViolation_NamesTheProjectAndTheFramework()
    {
        TargetedReport()
            .Single()
            .HumanBlock()
            .ShouldContain("Directory.Build.props:3 — Zphil.Host targets net48");
    }

    [Fact]
    public void HumanBlock_BannedPrivatePackage_ReadsAsAnyOtherPackage()
    {
        // The ban counts private references, and a private one is named the way every package is: privacy is a
        // fact about the declaration, not part of what the line says.
        BannedReport()
            .Single()
            .HumanBlock()
            .ShouldContain("Directory.Build.props:6 — Zphil.Domain references package StyleCop.Analyzers");
    }

    [Fact]
    public void HumanBlock_PropertyViolations_SayWhatEachFrameworksBuildHolds()
    {
        // One build sets nothing and one sets another value: the first points at the project file, where the
        // declaration would go, and the second at the definition that won.
        string block = PropertyReport()
            .Single()
            .HumanBlock();

        block.ShouldContain("Bus.csproj:1 — Zphil.Bus does not set Nullable for net472");
        block.ShouldContain("Directory.Build.props:7 — Zphil.Bus sets Nullable to disable for net8.0");
    }

    [Fact]
    public void HumanBlock_MissingFrameworkViolation_SaysTheProjectDoesNotTargetIt()
    {
        // The framework slot is filled either way, so the wording is what tells the floor's finding from the
        // ceiling's: a reader must not be told the project targets the one framework it lacks.
        FlooredReport()
            .Single()
            .HumanBlock()
            .ShouldContain("Directory.Build.props:3 — Zphil.Host does not target netstandard2.0");
    }

    [Fact]
    public void HumanBlock_UnlocatedPackagingViolation_ListsTheProjectWithNoSite()
    {
        // A fact nothing declared has nowhere to point, so the project is listed rather than dropped — the
        // same unlocated arm an empty subject and a rule error take.
        CodebaseModel siteless = ProjectFacts.Solution(ProjectFacts.Project("Zphil.Loose", isPackable: true));

        string block = Checker.Run(siteless, arch => arch.Rule("packaging/internal-not-shipped")
                .Enforce(arch.Projects.Matching("*").MustNotBePackable())
                .Because("b"))
            .Single()
            .HumanBlock();

        block.ShouldContain("  Zphil.Loose");
        block.ShouldNotContain(" — Zphil.Loose");
    }

    [Fact]
    public void Json_PackagingViolation_CarriesSubjectProjectAndOmitsThePackageSlot()
    {
        PackableReport()
            .ShouldRenderProjectShapeViolation("projectShape", "Zphil.Internal");
    }

    [Fact]
    public void Json_PerPackageViolation_CarriesBothSlots()
    {
        PackagedReport()
            .ShouldRenderProjectShapeViolation("projectShape", "Zphil.Domain", "Serilog");
    }

    [Fact]
    public void Json_PerFrameworkViolation_CarriesTheFrameworkAndOmitsThePackage()
    {
        TargetedReport()
            .ShouldRenderProjectShapeViolation("projectShape", "Zphil.Host", framework: "net48");
    }

    [Fact]
    public void Json_BannedPrivatePackage_CarriesThePackage()
    {
        BannedReport()
            .ShouldRenderProjectShapeViolation("projectShape", "Zphil.Domain", "StyleCop.Analyzers");
    }

    [Fact]
    public void Json_PropertyViolation_CarriesThePropertyTheFrameworkAndTheValueHeld()
    {
        string json = PropertyReport().JsonReport();
        IReadOnlyList<JsonElement> violations = CheckJson.Violations(json, "build/nullable");

        violations.Select(violation => $"{violation.GetProperty("property").GetString()}@"
                                       + $"{violation.GetProperty("framework").GetString()}="
                                       + (violation.TryGetProperty("propertyValue", out JsonElement value) ? value.GetString() : "(unset)"))
            .ShouldBe(["Nullable@net472=(unset)", "Nullable@net8.0=disable"]);
    }

    [Fact]
    public void Json_MissingFrameworkViolation_CarriesTheFramework()
    {
        // The same key as the ceiling's: the rule's sentence, beside it in the document, says which reading
        // applies.
        FlooredReport()
            .ShouldRenderProjectShapeViolation("projectShape", "Zphil.Host", framework: "netstandard2.0");
    }

    [Fact]
    public void Json_ReportWithNoProjectRule_CarriesNeitherSlot()
    {
        // The additive-key claim: the two slots exist for project violations and are absent everywhere else,
        // which is what holds the schema at version 3 for every spec that has no project rule.
        CheckReport report = Checker.Run(
            "namespace App { public class Thing {} }",
            arch => arch.Rule("naming/things")
                .Enforce(arch.Namespace("App.*").MustHaveSuffix("Service"))
                .Because("b"));

        string json = report.JsonReport();
        JsonElement violation = CheckJson.Violations(json, "naming/things")[0];

        violation.TryGetProperty("subjectProject", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("package", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("framework", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("property", out _)
            .ShouldBeFalse();
        violation.TryGetProperty("propertyValue", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void Sarif_PackagingViolation_MessageIsTheProjectName()
    {
        PackableReport()
            .ToSarif()
            .ShouldBeOneErrorSaying("Zphil.Internal");
    }

    [Fact]
    public void Sarif_PerPackageViolation_MessageNamesTheProjectAndThePackage()
    {
        PackagedReport()
            .ToSarif()
            .ShouldBeOneErrorSaying("Zphil.Domain references package Serilog");
    }

    [Fact]
    public void Sarif_PerFrameworkViolation_MessageNamesTheProjectAndTheFramework()
    {
        TargetedReport()
            .ToSarif()
            .ShouldBeOneErrorSaying("Zphil.Host targets net48");
    }

    [Fact]
    public void Sarif_BannedPrivatePackage_MessageNamesTheProjectAndThePackage()
    {
        BannedReport()
            .ToSarif()
            .ShouldBeOneErrorSaying("Zphil.Domain references package StyleCop.Analyzers");
    }

    [Fact]
    public void Sarif_PropertyViolations_SayWhatEachFrameworksBuildHolds()
    {
        IReadOnlyList<JsonElement> results = PropertyReport()
            .ToSarif()
            .SarifResults();

        results.Select(result => $"{result.GetProperty("level").GetString()}: "
                                 + $"{result.GetProperty("message").GetProperty("text").GetString()}")
            .ShouldBe([
                "error: Zphil.Bus does not set Nullable for net472",
                "error: Zphil.Bus sets Nullable to disable for net8.0"
            ]);
    }

    [Fact]
    public void PropertySetToNothing_ReadsAsAnEmptyValue_WithNoFrameworkWhereNoneWasToldApart()
    {
        // A definition that sets the property to whitespace is a value the build holds, not an absence, and it
        // is named so rather than printed as a blank; an evaluation that told no framework apart names none.
        CodebaseModel emptied = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Zphil.Bus", evaluations: [ProjectFacts.Evaluation(null, "Bus.csproj", ("Nullable", " ", "Bus.csproj", 6))]));

        CheckReport report = Checker.Run(emptied, PropertyRule);

        string block = report.Single()
            .HumanBlock();
        block.ShouldContain("Bus.csproj:6 — Zphil.Bus sets Nullable to an empty value");
        block.ShouldNotContain("empty value for");
        report.ToSarif()
            .ShouldBeOneErrorSaying("Zphil.Bus sets Nullable to an empty value");
    }

    [Fact]
    public void Sarif_MissingFrameworkViolation_MessageSaysTheProjectDoesNotTargetIt()
    {
        FlooredReport()
            .ToSarif()
            .ShouldBeOneErrorSaying("Zphil.Host does not target netstandard2.0");
    }

    private static CheckReport PackableReport()
    {
        return Checker.Run(Packable, arch => arch.Rule("packaging/internal-not-shipped")
            .Enforce(arch.Projects.Matching("*").MustNotBePackable())
            .Because("An internal project on the feed is an API nobody meant to promise."));
    }

    private static CheckReport TargetedReport()
    {
        return Checker.Run(Targeted, arch => arch.Rule("packaging/supported-frameworks")
            .Enforce(arch.Projects.Matching("*").MustOnlyTarget("net8.0"))
            .Because("A framework nobody supports is a build nobody can reproduce."));
    }

    private static CheckReport BannedReport()
    {
        return Checker.Run(Analyzed, arch => arch.Rule("packaging/no-stylecop")
            .Enforce(arch.Projects.Matching("*").MustNotReferencePackages("StyleCop.*"))
            .Because("The house analyzers replaced StyleCop, and two rule sets disagree on every file."));
    }

    private static CheckReport PropertyReport()
    {
        return Checker.Run(Configured, PropertyRule);
    }

    private static void PropertyRule(Arch arch)
    {
        arch.Rule("build/nullable")
            .Enforce(arch.Projects.Matching("*").MustSetProperty("Nullable", "enable"))
            .Because("Callers read a library's nullability from its annotations.");
    }

    private static CheckReport FlooredReport()
    {
        return Checker.Run(Targeted, arch => arch.Rule("packaging/standard-floor")
            .Enforce(arch.Projects.Matching("*").MustTarget("netstandard2.0"))
            .Because("Every package has to load on the net48 hosts the estate still runs."));
    }

    private static CheckReport PackagedReport()
    {
        return Checker.Run(Packaged, arch => arch.Rule("packaging/domain-pure")
            .Enforce(arch.Projects.Matching("*").MustReferenceNoPackages())
            .Because("The domain must not take a dependency the rest of the estate has to carry."));
    }
}
