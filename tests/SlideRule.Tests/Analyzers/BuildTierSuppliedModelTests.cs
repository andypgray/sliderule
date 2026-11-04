using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Analyzers;
using Xunit;

namespace SlideRule.Tests.Analyzers;

/// <summary>
///     A model the analyzer carries has no directory, so a rule it would check that names a baseline or selects
///     types by a path glob is refused at the start as the analyzer's fault, naming the rule, rather than faulting
///     on a descriptor the host never declared or checking nothing at all. A rule the compilation declines is
///     never checked, so it refuses nothing.
/// </summary>
/// <remarks>
///     No shipped host carries such a rule, so the host here is made for the test: the engine under a supplied
///     policy that declares one descriptor for the rule and the fault, as the guidance analyzer does.
/// </remarks>
public sealed class BuildTierSuppliedModelTests
{
    // Release tracking is for an analyzer that ships the descriptor; this one never leaves the test.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor RuleDescriptor = new(
        "SR9998", "The carried rule", "{0}", "SlideRule.Test", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GlobRuleDescriptor = new(
        "SR9997", "The carried glob rule", "{0}", "SlideRule.Test", DiagnosticSeverity.Warning, isEnabledByDefault: true);
#pragma warning restore RS2008

    private const string Refused = "The model this analyzer carries cannot check rule 'layering/web-through-the-domain': ";

    [Theory]
    [InlineData("a baseline", "it names a baseline, and a carried model has no directory to find one in")]
    [InlineData("a path glob on its subject",
        "it selects types by a path glob, and a carried model has no directory to resolve the glob against")]
    [InlineData("a path glob in a layer's definition",
        "it selects types by a path glob, and a carried model has no directory to resolve the glob against")]
    [InlineData("a path glob in a layer a union defines a layer by",
        "it selects types by a path glob, and a carried model has no directory to resolve the glob against")]
    [InlineData("a path glob on one part of a union subject",
        "it selects types by a path glob, and a carried model has no directory to resolve the glob against")]
    public async Task ARuleNeedingADirectory_IsRefusedAsTheFault_NamingTheRule(string needing, string reason)
    {
        // Arrange: the own project breaks the rule, so a rule that needed a directory and was checked anyway
        // would have a finding to withhold or to lose.
        Compilation own = BuildTier.OwnCompilation();

        // Act
        IReadOnlyList<Diagnostic> guard = await DiagnoseAsync(own, BuildTier.EnforceRule);
        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync(own, RuleNeeding(needing));

        // Assert: the guard, the same law needing no directory, reports each of the own project's three sites.
        guard.ShouldReport(RuleDescriptor.Id, RuleDescriptor.Id, RuleDescriptor.Id);
        reported.ShouldBeTheOnly(BuildTierDescriptors.AnalyzerFault.Id)
            .ShouldSay(BuildTier.FaultMessageFor($"InvalidOperationException: {Refused}{reason}"));
    }

    private static Action<Arch> RuleNeeding(string needing)
    {
        return needing switch
        {
            "a baseline" => BuildTier.MigrateRule,
            "a path glob on its subject" => arch => arch.Rule(BuildTier.RuleId)
                .Enforce(arch.Namespace("Web.*").Under("**").MustNotReference(arch.Layer("Data", "Data.*")))
                .Because(BuildTier.Because),
            "a path glob in a layer's definition" => arch => arch.Rule(BuildTier.RuleId)
                .Enforce(arch.Layer("Web", arch.Namespace("Web.*").Under("**")).MustNotReference(arch.Layer("Data", "Data.*")))
                .Because(BuildTier.Because),
            "a path glob in a layer a union defines a layer by" => arch => arch.Rule(BuildTier.RuleId)
                .Enforce(arch.Layer("Web", arch.AnyOf(arch.Layer("Pages", arch.Namespace("Web.*").Under("**")), arch.Namespace("Api.*")))
                    .MustNotReference(arch.Layer("Data", "Data.*")))
                .Because(BuildTier.Because),
            "a path glob on one part of a union subject" => arch => arch.Rule(BuildTier.RuleId)
                .Enforce(arch.AnyOf(arch.Namespace("Web.*").Under("**"), arch.Namespace("Api.*"))
                    .MustNotReference(arch.Layer("Data", "Data.*")))
                .Because(BuildTier.Because),
            _ => throw new ArgumentOutOfRangeException(nameof(needing), needing, "No such row.")
        };
    }

    [Theory]
    [InlineData("the project's own model declares it")]
    [InlineData("its descriptor is switched off")]
    public async Task ARuleNeedingADirectory_ThatTheCompilationDeclines_RefusesNothing(string declined)
    {
        // Arrange: the carried model holds the law beside a path-glob rule the compilation declines, so refusing
        // that rule would be the analyzer's own fault; the law reports alone.
        Compilation own = BuildTier.OwnCompilation();
        (Compilation compilation, AdditionalText[] files) = declined switch
        {
            "the project's own model declares it" =>
                (own, new[] { BuildTier.ModelFile(BuildTier.ModelText(DeclaresTheGlobRule)) }),
            "its descriptor is switched off" =>
                (BuildTier.WithSeverity(own, ReportDiagnostic.Suppress, GlobRuleDescriptor.Id), Array.Empty<AdditionalText>()),
            _ => throw new ArgumentOutOfRangeException(nameof(declined), declined, "No such row.")
        };

        // Act
        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync(compilation, LawBesideAGlobRule, files);

        // Assert: the law still reports each of the own project's three sites, and nothing faults.
        reported.ShouldReport(RuleDescriptor.Id, RuleDescriptor.Id, RuleDescriptor.Id);
    }

    // The law the guard rows check, beside a rule that needs a directory.
    private static void LawBesideAGlobRule(Arch arch)
    {
        Layer data = arch.Layer("Data", "Data.*");
        arch.Rule(BuildTier.RuleId)
            .Enforce(arch.Layer("Web", "Web.*").MustNotReference(data))
            .Because(BuildTier.Because)
            .Fix(BuildTier.Fix);
        arch.Rule(BuildTier.SecondRuleId)
            .Enforce(arch.Namespace("Web.*").Under("**").MustNotReference(data))
            .Because(BuildTier.Because);
    }

    // A project's own model, which declares the glob rule's ID and so takes the rule off the carried model.
    private static void DeclaresTheGlobRule(Arch arch)
    {
        arch.Rule(BuildTier.SecondRuleId)
            .Enforce(arch.Layer("Web", "Web.*").MustNotReference(arch.Layer("Data", "Data.*")))
            .Because(BuildTier.Because);
    }

    private static Task<IReadOnlyList<Diagnostic>> DiagnoseAsync(
        Compilation compilation, Action<Arch> define, params AdditionalText[] files)
    {
        var host = new SuppliedHost(BuildTier.ModelText(define));

        return BuildTier.DiagnoseAsync(host, compilation, files, cancellationToken: Ct);
    }

    // Handed to the driver directly. The export attribute would make the test assembly a compiler extension, which
    // the analyzer-authoring rules then hold to netstandard and to no Workspaces reference.
#pragma warning disable RS1001
    private sealed class SuppliedHost(string model) : DiagnosticAnalyzer
    {
        private readonly BuildTierEngine _engine = new(BuildTierPolicy.Supplied(
            SourceText.From(model, Encoding.UTF8),
            new Dictionary<string, DiagnosticDescriptor>(StringComparer.Ordinal)
            {
                [BuildTier.RuleId] = RuleDescriptor,
                [BuildTier.SecondRuleId] = GlobRuleDescriptor
            },
            BuildTierDescriptors.AnalyzerFault));

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(RuleDescriptor, GlobRuleDescriptor, BuildTierDescriptors.AnalyzerFault);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(
                GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.EnableConcurrentExecution();
            _engine.Register(context);
        }
    }
#pragma warning restore RS1001
}
