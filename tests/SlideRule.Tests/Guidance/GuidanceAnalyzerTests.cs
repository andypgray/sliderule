extern alias guidance;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Shouldly;
using SlideRule.Hosting;
using SlideRule.Tests.Analyzers;
using SlideRule.Tests.Analyzers.Parity;
using SlideRule.Tests.TestSupport;
using Xunit;
using GuidanceAnalyzer = guidance::SlideRule.Guidance.DotNet.GuidanceAnalyzer;
using GuidanceDiagnostics = guidance::SlideRule.Guidance.DotNet.GuidanceDiagnostics;

namespace SlideRule.Tests.Guidance;

/// <summary>
///     The guidance analyzer reports what the whole-solution check over its model reports, under each rule's
///     own ID, in a project of any name, and leaves a rule to a project's own model that declares it.
/// </summary>
/// <remarks>
///     The parity pin runs every rule at warning, whatever its shipped severity, because parity is a claim
///     about what the engine finds; which rules a build shows by default is a claim of its own, made on the
///     conforming project.
/// </remarks>
public sealed class GuidanceAnalyzerTests(ParityBeds beds) : IClassFixture<ParityBeds>
{
    private const string RulesProject = "GuidanceApp.Rules";

    private const string ConformingProject = "GuidanceApp.Conforming";

    // The guidance analyzer as the parity pin asks it: handed no file, because the model it checks is the one
    // it embeds, and with every rule at warning. A warning is a finding only under its own rule's descriptor,
    // so a warning under any other is a stranger.
    private static readonly ParityAnalyzer Guidance = new(
        () => new GuidanceAnalyzer(),
        diagnostic => GuidanceDiagnostics.ByRule.TryGetValue(diagnostic.Property("rule") ?? "", out DiagnosticDescriptor? own)
                      && own.Id == diagnostic.Id,
        UncheckedId: null,
        TakesTheSubjectsFiles: false,
        AllEnabled);

    [Fact]
    public async Task OnTheBed_ReportsWhatTheWholeSolutionCheckFinds()
    {
        ParitySubject subject = await beds.SubjectAsync(
            "GuidanceApp", GuidanceModelTests.Model, FixtureBeds.GuidanceAppSolution);

        // The difference guard: a bed that broke no rule would hold parity on two empty sets.
        subject.Expectation.Keys.Select(key => key.Rule).Distinct().Order(StringComparer.Ordinal)
            .ShouldBe(GuidanceModelTests.Model.Rules.Select(rule => rule.Id).Order(StringComparer.Ordinal));

        await subject.ShouldHoldOnBothLegsAsync(Guidance, Ct);
    }

    [Fact]
    public async Task TheConformingProject_AtTheShippedSeverities_ReportsNothing()
    {
        Compilation conforming = await CompilationAsync(ConformingProject);

        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync(conforming);

        reported.ShouldBeSilent();
    }

    [Fact]
    public async Task TheAsyncRules_ReportASignatureWhereItIsDeclared_NeverWhereItIsInherited()
    {
        // Of the types read here, the bed declares two signatures that break both async rules, an interface method
        // and an abstract one, beside an implementation of the one, an override of the other and a framework
        // interface's implementation. A message opens with the rule and the member it reports.
        Compilation rules = await CompilationAsync(RulesProject);
        string[] types = ["IExporter", "Exporter", "Job", "NightlyJob", "Disposer"];

        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync(rules);

        reported
            .Select(diagnostic => $"{diagnostic.Id} {diagnostic.GetMessage(CultureInfo.InvariantCulture).Split(". ")[0]}")
            .Where(line => types.Any(type => line.Contains($"GuidanceApp.Rules.{type}.")))
            .ShouldBe([
                "SR3005 naming/async-suffix: GuidanceApp.Rules.IExporter.Export()",
                "SR3007 async/accept-cancellation: GuidanceApp.Rules.IExporter.Export()",
                "SR3005 naming/async-suffix: GuidanceApp.Rules.Job.Run()",
                "SR3007 async/accept-cancellation: GuidanceApp.Rules.Job.Run()"
            ], ignoreOrder: true);
    }

    [Fact]
    public async Task AProjectOfAnyAssemblyName_IsGoverned()
    {
        Compilation rules = await CompilationAsync(RulesProject);

        IReadOnlyList<Diagnostic> named = await DiagnoseAsync(rules);
        IReadOnlyList<Diagnostic> renamed = await DiagnoseAsync(rules.WithAssemblyName("Contoso.Anything"));

        named.ShouldNotBeEmpty();
        renamed.ShouldReportTheSameAs(named);
    }

    [Fact]
    public async Task ARuleTheProjectsOwnModelDeclares_IsLeftToIt_AndOnlyThatRule()
    {
        // Arrange: the project's model declares the suffix rule over a subject that matches nothing here, so
        // the only way the rule can go quiet is by its ID, and declares the blocking rule through a predicate,
        // which the file names without carrying.
        Compilation rules = await CompilationAsync(RulesProject);
        string projectModel = BuildTier.ModelText(arch =>
        {
            arch.Rule("naming/async-suffix")
                .Enforce(arch.Types.InNamespace("Elsewhere.*").Methods.MustHaveSuffix("Async"))
                .Because("The project names its own async methods.");
            arch.Rule("async/no-sync-over-async")
                .Enforce(arch.Types.Where(_ => false, "that the project picks").MustBeSealed())
                .Because("The project blocks where it chooses to.");
        });

        // Act
        IReadOnlyList<Diagnostic> alone = await DiagnoseAsync(rules);
        IReadOnlyList<Diagnostic> beside = await DiagnoseAsync(rules, BuildTier.ModelFile(projectModel));

        // Assert
        string[] declined = ["naming/async-suffix", "async/no-sync-over-async"];
        alone.Select(diagnostic => diagnostic.Property("rule")).ShouldContain("naming/async-suffix");
        alone.Select(diagnostic => diagnostic.Property("rule")).ShouldContain("async/no-sync-over-async");
        beside.ShouldReportTheSameAs(alone.Where(diagnostic => !declined.Contains(diagnostic.Property("rule"))));
    }

    [Fact]
    public async Task AProjectModelThatWillNotRead_DeclinesNothing_AndIsNoFault()
    {
        Compilation rules = await CompilationAsync(RulesProject);

        IReadOnlyList<Diagnostic> alone = await DiagnoseAsync(rules);
        IReadOnlyList<Diagnostic> beside = await DiagnoseAsync(rules, BuildTier.ModelFile("{ not a model"));

        beside.ShouldReportTheSameAs(alone);
    }

    [Fact]
    public async Task ASeverityGivenToOneRule_MovesThatRuleAlone()
    {
        Compilation rules = await CompilationAsync(RulesProject);
        Compilation tuned = BuildTier.WithSeverity(
            BuildTier.WithSeverity(rules, ReportDiagnostic.Suppress, GuidanceDiagnostics.AsyncSuffix.Id),
            ReportDiagnostic.Error,
            GuidanceDiagnostics.NoSyncOverAsync.Id);

        IReadOnlyList<Diagnostic> shipped = await DiagnoseAsync(rules);
        IReadOnlyList<Diagnostic> moved = await DiagnoseAsync(tuned);

        IEnumerable<string> expected = shipped
            .Where(diagnostic => diagnostic.Id != GuidanceDiagnostics.AsyncSuffix.Id)
            .Select(diagnostic => diagnostic.Id == GuidanceDiagnostics.NoSyncOverAsync.Id
                ? $"error {diagnostic.Describe()}"
                : $"{diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Describe()}");
        moved.Select(diagnostic => $"{diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Describe()}")
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("in the compilation options")]
    [InlineData("in an .editorconfig")]
    [InlineData("in a global config")]
    [InlineData("by the category key")]
    [InlineData("by the bulk key")]
    public async Task ARuleSwitchedOffInEveryTree_IsNeverChecked(string form)
    {
        // Arrange: every rule off in one form and the fault left on, with the check seam armed to fault, so a
        // rule checked anyway reports the fault. The guard turns one rule back on.
        Compilation rules = await CompilationAsync(RulesProject);
        (Compilation off, AnalyzerConfigOptionsProvider options) = EveryRuleOff(rules, form);
        Compilation oneOn = BuildTier.WithSeverity(off, ReportDiagnostic.Warn, GuidanceDiagnostics.AcceptCancellation.Id);

        // Act
        IReadOnlyList<Diagnostic> reported =
            await BuildTier.DiagnoseAsync(new GuidanceAnalyzer(BuildTier.Boom), off, options, Ct);
        IReadOnlyList<Diagnostic> guard =
            await BuildTier.DiagnoseAsync(new GuidanceAnalyzer(BuildTier.Boom), oneOn, options, Ct);

        // Assert
        guard.ShouldBeTheOnly(GuidanceDiagnostics.AnalyzerFault.Id);
        reported.ShouldBeSilent();
    }

    [Theory]
    [InlineData("in the compilation options")]
    [InlineData("in an .editorconfig")]
    [InlineData("in one file's .editorconfig section, off in a global config")]
    [InlineData("in a global config")]
    [InlineData("by the category key, the bulk key off")]
    [InlineData("by the bulk key")]
    public async Task ARuleSwitchedOnInAnyForm_StillReports(string form)
    {
        // Arrange: every other rule off by its own ID, which the bulk keys do not override.
        string id = GuidanceDiagnostics.AcceptCancellation.Id;
        Compilation rules = await CompilationAsync(RulesProject);
        (Compilation configured, AnalyzerConfigOptionsProvider options) = OneRuleOn(rules, form);

        // Act
        IReadOnlyList<Diagnostic> everyRule = await DiagnoseAsync(AllEnabled(rules));
        IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(new GuidanceAnalyzer(), configured, options, Ct);

        // Assert
        List<Diagnostic> expected = everyRule
            .Where(diagnostic => diagnostic.Id == id)
            .ToList();
        expected.ShouldNotBeEmpty($"The bed breaks {id}, or this row could pass on two empty sets.");
        reported.ShouldReportTheSameAs(expected);
    }

    [Fact]
    public async Task AFault_IsReportedUnderTheFaultsOwnId_AndNothingElse()
    {
        Compilation rules = await CompilationAsync(RulesProject);

        IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(
            new GuidanceAnalyzer(BuildTier.Boom), rules, [], cancellationToken: Ct);

        reported.ShouldBeTheOnly(GuidanceDiagnostics.AnalyzerFault.Id);
    }

    [Fact]
    public void TheDescriptorTable_MatchesTheModel_RuleForRule()
    {
        GuidanceDiagnostics.ByRule.Keys.Order(StringComparer.Ordinal)
            .ShouldBe(GuidanceModelTests.Model.Rules.Select(rule => rule.Id).Order(StringComparer.Ordinal));

        foreach (ArchRule rule in GuidanceModelTests.Model.Rules)
        {
            DiagnosticDescriptor descriptor = GuidanceDiagnostics.ByRule[rule.Id];
            descriptor.HelpLinkUri.ShouldBe(rule.Citation, $"{descriptor.Id} links the page {rule.Id} cites.");
            descriptor.Description.ToString().ShouldBe(rule.Because, $"{descriptor.Id} describes {rule.Id}.");
        }
    }

    [Fact]
    public void TheDescriptors_AreTheRulesInOrder_ThenTheFault()
    {
        GuidanceDiagnostics.All.Select(descriptor => descriptor.Id)
            .ShouldBe(["SR3001", "SR3002", "SR3003", "SR3004", "SR3005", "SR3006", "SR3007", "SR3008", "SR3093"]);
        GuidanceDiagnostics.All.Take(8).ShouldBe(GuidanceModelTests.Model.Rules.Select(rule => GuidanceDiagnostics.ByRule[rule.Id]));
    }

    [Fact]
    public void EveryDescriptor_ShipsEnabled_AtWarning_ButAcceptCancellationAtInfo()
    {
        GuidanceDiagnostics.All
            .Select(descriptor => $"{descriptor.Id} {descriptor.IsEnabledByDefault} {descriptor.DefaultSeverity}")
            .ShouldBe([
                "SR3001 True Warning", "SR3002 True Warning", "SR3003 True Warning", "SR3004 True Warning",
                "SR3005 True Warning", "SR3006 True Warning", "SR3007 True Info", "SR3008 True Warning",
                "SR3093 True Warning"
            ]);
    }

    [Fact]
    public void TheReleaseFiles_ListExactlyTheSupportedDescriptors()
    {
        // The release-tracking rules report a descriptor missing from these files only as a warning the build does
        // not fail on, so this is what holds the files to exactly the descriptors the analyzer supports.
        List<string> listed = RepoRoot.ReadLines("src/SlideRule.Guidance.DotNet/AnalyzerReleases.Shipped.md")
            .Concat(RepoRoot.ReadLines("src/SlideRule.Guidance.DotNet/AnalyzerReleases.Unshipped.md"))
            .Where(line => line.StartsWith("SR", StringComparison.Ordinal))
            .Select(line => line.Split('|')[0].Trim())
            .ToList();

        listed.ShouldBe(GuidanceDiagnostics.All.Select(descriptor => descriptor.Id));
    }

    [Fact]
    public void TheEmbeddedModel_IsTheCommittedFile()
    {
        Assembly assembly = typeof(GuidanceAnalyzer).Assembly;
        assembly.GetManifestResourceNames().ShouldBe([GuidanceAnalyzer.ModelResource]);

        GuidanceAnalyzer.EmbeddedModel().ToString()
            .ShouldBe(RepoRoot.ReadText(GuidanceModelTests.ModelPath));
    }

    [Fact]
    public void TheAssembly_ExportsTheGuidanceAnalyzerAlone()
    {
        typeof(GuidanceAnalyzer).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0)
            .Select(type => type.FullName)
            .ShouldBe([typeof(GuidanceAnalyzer).FullName]);
    }

    private async Task<Compilation> CompilationAsync(string label)
    {
        IReadOnlyList<ParityCompilation> compilations = await beds.CompilationsAsync(FixtureBeds.GuidanceAppSolution);

        return compilations.Single(compilation => compilation.Label == label).Compilation;
    }

    // The guidance analyzer, armed with no seam, over a compilation the build hands files.
    private static Task<IReadOnlyList<Diagnostic>> DiagnoseAsync(Compilation compilation, params AdditionalText[] files)
    {
        return BuildTier.DiagnoseAsync(new GuidanceAnalyzer(), compilation, files, cancellationToken: Ct);
    }

    // Every rule at warning, whatever its shipped severity.
    private static Compilation AllEnabled(Compilation compilation)
    {
        return BuildTier.WithSeverity(
            compilation, ReportDiagnostic.Warn, [.. GuidanceDiagnostics.All.Select(descriptor => descriptor.Id)]);
    }

    private static string[] RuleIds => [.. GuidanceDiagnostics.ByRule.Values.Select(descriptor => descriptor.Id)];

    // Every rule switched off in the one form, with the fault left on wherever the form would reach it too.
    private static (Compilation Compilation, AnalyzerConfigOptionsProvider Options) EveryRuleOff(
        Compilation compilation, string form)
    {
        string byId = string.Join("\n", RuleIds.Select(id => $"dotnet_diagnostic.{id}.severity = none"));
        var faultOn = $"dotnet_diagnostic.{GuidanceDiagnostics.AnalyzerFault.Id}.severity = warning";
        return form switch
        {
            "in the compilation options" => BuildTier.Configured(
                BuildTier.WithSeverity(compilation, ReportDiagnostic.Suppress, RuleIds)),
            "in an .editorconfig" => BuildTier.Configured(compilation, EditorConfig(compilation, "*.cs", byId)),
            "in a global config" => BuildTier.Configured(compilation, GlobalConfig(compilation, byId)),
            "by the category key" => BuildTier.Configured(compilation, EditorConfig(
                compilation, "*.cs", $"dotnet_analyzer_diagnostic.category-SlideRule.Guidance.severity = none\n{faultOn}")),
            "by the bulk key" => BuildTier.Configured(compilation, EditorConfig(
                compilation, "*.cs", $"dotnet_analyzer_diagnostic.severity = none\n{faultOn}")),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, "No such row.")
        };
    }

    // The cancellation rule switched on in the one form, and every other rule off by its own ID. It ships enabled,
    // at Info, so the bulk keys reach it as they reach a rule shipped at warning.
    private static (Compilation Compilation, AnalyzerConfigOptionsProvider Options) OneRuleOn(
        Compilation compilation, string form)
    {
        string id = GuidanceDiagnostics.AcceptCancellation.Id;
        Compilation othersOff = BuildTier.WithSeverity(
            compilation, ReportDiagnostic.Suppress, [.. RuleIds.Where(other => other != id)]);
        var byId = $"dotnet_diagnostic.{id}.severity = warning";

        return form switch
        {
            "in the compilation options" => BuildTier.Configured(BuildTier.WithSeverity(othersOff, ReportDiagnostic.Warn, id)),
            "in an .editorconfig" => BuildTier.Configured(othersOff, EditorConfig(othersOff, "*.cs", byId)),
            "in one file's .editorconfig section, off in a global config" => BuildTier.Configured(
                othersOff,
                GlobalConfig(othersOff, $"dotnet_diagnostic.{id}.severity = none"),
                EditorConfig(othersOff, "Violations.cs", byId)),
            "in a global config" => BuildTier.Configured(othersOff, GlobalConfig(othersOff, byId)),
            "by the category key, the bulk key off" => BuildTier.Configured(othersOff, EditorConfig(
                othersOff, "*.cs",
                "dotnet_analyzer_diagnostic.severity = none\n"
                + "dotnet_analyzer_diagnostic.category-SlideRule.Guidance.severity = warning")),
            "by the bulk key" => BuildTier.Configured(othersOff, EditorConfig(
                othersOff, "*.cs", "dotnet_analyzer_diagnostic.severity = warning")),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, "No such row.")
        };
    }

    // A root .editorconfig beside the project's one source file, with one section.
    private static (string Path, string Text) EditorConfig(Compilation compilation, string section, string body)
    {
        return (Path.Combine(ProjectDirectory(compilation), ".editorconfig"), $"root = true\n\n[{section}]\n{body}\n");
    }

    private static (string Path, string Text) GlobalConfig(Compilation compilation, string body)
    {
        return (Path.Combine(ProjectDirectory(compilation), ".globalconfig"), $"is_global = true\n{body}\n");
    }

    private static string ProjectDirectory(Compilation compilation)
    {
        SyntaxTree source = compilation.SyntaxTrees.Single(tree => Path.GetFileName(tree.FilePath) == "Violations.cs");

        return Path.GetDirectoryName(source.FilePath)!;
    }
}
