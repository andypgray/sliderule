using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Shouldly;
using SlideRule.Analyzers;
using SlideRule.SpecAnalyzers;
using SlideRule.SpecCompletion;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Analyzers;

/// <summary>
///     The firewall between each analyzer assembly and the copy of SlideRule and
///     SlideRule.Extraction the compiler binds it to, held over the built assemblies' IL.
/// </summary>
/// <remarks>
///     <para>
///         A project carrying the contract package and the analyzer package at two versions runs one analyzer
///         against the other package's copy of Core, and a member that moved between the two throws when the
///         JIT compiles the method naming it, at that method's call site in its caller. What the analyzer then
///         reports depends only on where each reference sits, which the IL shows and a run over one copy of
///         Core cannot: so the rules <see cref="CoreFirewall" /> states are read off the assemblies as built,
///         and every breach is listed with the rule it breaks and what it names.
///     </para>
///     <para>
///         The last fact is the walk's own difference guard. A probe written to break each rule once, and to
///         hold each one once beside the breach, has to come back as exactly those breaches: a walk that
///         could not see a rule would pass every analyzer here on silence.
///     </para>
/// </remarks>
public sealed class AnalyzerCoreFirewallTests
{
    // The build analyzer's fault path: where a fault or a held fault becomes the warning, and the factories of
    // the warning's two forms.
    private static readonly string[] BuildFaultPath =
    [
        "SlideRule.Analyzers.BuildTierAnalyzer.ReportFault",
        "SlideRule.Analyzers.BuildTierAnalyzer.ReportHeldFault",
        "SlideRule.Analyzers.BuildTierDiagnostics.AnalyzerFailed",
        "SlideRule.Analyzers.BuildTierDiagnostics.VersionsMismatched"
    ];

    // The spec analyzer's, which reports from the guards themselves through the two factories.
    private static readonly string[] SpecFaultPath =
    [
        "SlideRule.SpecAnalyzers.SpecLiteralDiagnostics.AnalyzerFailed",
        "SlideRule.SpecAnalyzers.SpecLiteralDiagnostics.VersionsMismatched"
    ];

    // Every rule broken, each beside a shape that holds it, so the expected list says both what the walk
    // sees and what it must not: Filtered and CaughtWhole are the two guards, Counted's lambda and
    // BoundaryLeaves' state machine belong to their boundaries, Fields.Rule is a reference-typed field, and
    // the third registration in Initialize hands Roslyn a clean entry point. Start's lambda is created inside
    // a boundary and is still no part of it, because Roslyn is handed it.
    private const string ProbeSource =
        """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;
        using System.Runtime.CompilerServices;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.Diagnostics;
        using Microsoft.CodeAnalysis.Text;
        using SlideRule.Hosting;
        using SlideRule.Rendering;
        using SlideRule.Validation;

        namespace Probe;

        public static class Calls
        {
            public static string NamesCore() => PathFormat.Relative("a", "a/b");

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static string Boundary() => PathFormat.Relative("a", "a/b");

            public static string Unguarded() => Boundary();

            public static string InFinally()
            {
                try { return Boundary(); }
                finally { }
            }

            public static string NarrowCatch()
            {
                try { return Boundary(); }
                catch (InvalidOperationException) { return ""; }
            }

            public static string Filtered()
            {
                try { return Boundary(); }
                catch (Exception exception) when (exception is not OperationCanceledException) { return ""; }
            }

            public static string CaughtWhole()
            {
                try { return Boundary(); }
                catch (Exception) { return ""; }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Counted(IReadOnlyList<string> paths) => paths.Count(path => PathFormat.Contains("a", path));

            public static Func<string, bool> Loose() => path => PathFormat.Contains("a", path);

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static IEnumerable<string> BoundaryLeaves(IEnumerable<string> paths)
            {
                foreach (string path in paths) yield return PathFormat.Relative("a", path);
            }

            public static IEnumerable<string> LooseLeaves(IEnumerable<string> paths)
            {
                foreach (string path in paths) yield return PathFormat.Relative("a", path);
            }
        }

        internal sealed class Fields
        {
            public SpecValidationErrorCode Code;
            public ImmutableArray<ArchRule> Rules;
            public ArchRule? Rule;
            public static ArchRule? Cached;
        }

        internal readonly struct Carrier(SpecValidationErrorCode code)
        {
            public SpecValidationErrorCode Code { get; } = code;
        }

        public static class Holder
        {
            public static int Held()
            {
                Carrier carrier = default;
                return carrier.GetHashCode();
            }

            public static object Compares() => new RuleComparer();
        }

        internal sealed class RuleComparer : IComparer<ArchRule>
        {
            public int Compare(ArchRule? x, ArchRule? y) => 0;
        }

        public static class Statics
        {
            public static readonly string Relative = PathFormat.Relative("a", "a/b");
        }

        public static class Faults
        {
            public static string Report(Exception exception) => exception.Message + Tail();

            private static string Tail() => PathFormat.Relative("a", "a/b");
        }

        [DiagnosticAnalyzer(LanguageNames.CSharp)]
        public sealed class Analyzer : DiagnosticAnalyzer
        {
            private static readonly SourceTextValueProvider<string> Texts = new(Compute);

            public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                get => ImmutableArray<DiagnosticDescriptor>.Empty;
            }

            public override void Initialize(AnalysisContext context)
            {
                context.RegisterCompilationStartAction(Start);
                context.RegisterCompilationStartAction(
                    [MethodImpl(MethodImplOptions.NoInlining)] static (CompilationStartAnalysisContext start) => { });
                context.RegisterCompilationStartAction(start => Clean(start));
            }

            public static void Forward(AnalysisContext context, Action<CompilationStartAnalysisContext> action)
            {
                context.RegisterCompilationStartAction(action);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Start(CompilationStartAnalysisContext context)
            {
                context.RegisterCompilationEndAction(end => PathFormat.Contains("a", "a/b"));
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static string Compute(SourceText text) => text.ToString();

            private static void Clean(CompilationStartAnalysisContext context)
            {
            }
        }
        """;

    [Fact]
    public void TheBuildAnalyzer_NamesCoreOnlyInsideBoundariesItsEntryPointsGuard()
    {
        CoreFirewall.Inspect(typeof(BuildTierAnalyzer).Assembly, BuildFaultPath)
            .ShouldHoldTheFirewall("SlideRule.Analyzers");
    }

    [Fact]
    public void TheSpecAnalyzer_NamesCoreOnlyInsideBoundariesItsEntryPointsGuard()
    {
        CoreFirewall.Inspect(typeof(SpecLiteralAnalyzer).Assembly, SpecFaultPath)
            .ShouldHoldTheFirewall("SlideRule.SpecAnalyzers");
    }

    [Fact]
    public void TheCompletionProvider_ReferencesNeitherCoreNorExtraction()
    {
        ImmutableArray<byte> image = ImmutableArray.Create(
            File.ReadAllBytes(typeof(SpecSlotCompletionProvider).Assembly.Location));

        CoreFirewall.GuardedReferences(image)
            .ShouldBeEmpty("The completion provider must reference neither SlideRule nor its extraction.");
        CoreFirewall.Inspect(image, [])
            .ShouldHoldTheFirewall("SlideRule.SpecCompletion");
    }

    [Fact]
    public void TheWalk_ReportsEachRuleWhereAProbeBreaksItAndNothingWhereItHolds()
    {
        ImmutableArray<byte> image = Emitted(ProbeSource);

        CoreFirewall.Inspect(image, ["Probe.Faults.Report"])
            .ShouldBreachExactly(
                "Probe.Analyzer+<>c.<Start>b__5_0: rule 1, names Core outside a boundary: "
                + "SlideRule.Rendering.PathFormat.Contains",
                "Probe.Analyzer..cctor: rule 2, calls a boundary outside a guard: Probe.Analyzer.Compute",
                "Probe.Analyzer..cctor: rule 3, hands Roslyn a boundary: "
                + "SourceTextValueProvider`1..ctor is handed Probe.Analyzer.Compute",
                "Probe.Analyzer.Forward: rule 3, hands Roslyn a boundary: "
                + "AnalysisContext.RegisterCompilationStartAction is handed a delegate the walk cannot follow",
                "Probe.Analyzer.Initialize: rule 2, calls a boundary outside a guard: "
                + "Probe.Analyzer+<>c.<Initialize>b__3_0, Probe.Analyzer.Start",
                "Probe.Analyzer.Initialize: rule 3, hands Roslyn a boundary: "
                + "AnalysisContext.RegisterCompilationStartAction is handed Probe.Analyzer+<>c.<Initialize>b__3_0",
                "Probe.Analyzer.Initialize: rule 3, hands Roslyn a boundary: "
                + "AnalysisContext.RegisterCompilationStartAction is handed Probe.Analyzer.Start",
                "Probe.Analyzer.get_SupportedDiagnostics: rule 3, hands Roslyn a boundary: Roslyn calls it directly",
                "Probe.Calls+<>c.<Loose>b__8_0: rule 1, names Core outside a boundary: "
                + "SlideRule.Rendering.PathFormat.Contains",
                "Probe.Calls+<LooseLeaves>d__10.MoveNext: rule 1, names Core outside a boundary: "
                + "SlideRule.Rendering.PathFormat.Relative",
                "Probe.Calls.InFinally: rule 2, calls a boundary outside a guard: Probe.Calls.Boundary",
                "Probe.Calls.NamesCore: rule 1, names Core outside a boundary: "
                + "SlideRule.Rendering.PathFormat.Relative",
                "Probe.Calls.NarrowCatch: rule 2, calls a boundary outside a guard: Probe.Calls.Boundary",
                "Probe.Calls.Unguarded: rule 2, calls a boundary outside a guard: Probe.Calls.Boundary",
                "Probe.Carrier: rule 5, loading the type binds Core: "
                + "field Code is SlideRule.Validation.SpecValidationErrorCode",
                "Probe.Carrier..ctor: rule 1, names Core outside a boundary: "
                + "Probe.Carrier (whose field Code is SlideRule.Validation.SpecValidationErrorCode), "
                + "SlideRule.Validation.SpecValidationErrorCode, "
                + "SlideRule.Validation.SpecValidationErrorCode (through Probe.Carrier.Code)",
                "Probe.Carrier.get_Code: rule 1, names Core outside a boundary: "
                + "Probe.Carrier (whose field Code is SlideRule.Validation.SpecValidationErrorCode), "
                + "SlideRule.Validation.SpecValidationErrorCode, "
                + "SlideRule.Validation.SpecValidationErrorCode (through Probe.Carrier.Code)",
                "Probe.Faults.Tail: rule 1, names Core outside a boundary: "
                + "SlideRule.Rendering.PathFormat.Relative",
                "Probe.Faults.Tail: rule 4, the fault path names Core: "
                + "SlideRule.Rendering.PathFormat.Relative (reached from Probe.Faults.Report)",
                "Probe.Fields: rule 5, loading the type binds Core: "
                + "field Code is SlideRule.Validation.SpecValidationErrorCode, "
                + "field Rules is System.Collections.Immutable.ImmutableArray<SlideRule.Hosting.ArchRule>, "
                + "static field Cached is SlideRule.Hosting.ArchRule",
                "Probe.Holder.Compares: rule 1, names Core outside a boundary: Probe.RuleComparer "
                + "(whose interface System.Collections.Generic.IComparer<SlideRule.Hosting.ArchRule>)",
                "Probe.Holder.Held: rule 1, names Core outside a boundary: "
                + "Probe.Carrier (whose field Code is SlideRule.Validation.SpecValidationErrorCode)",
                "Probe.RuleComparer.Compare: rule 1, names Core outside a boundary: SlideRule.Hosting.ArchRule",
                "Probe.Statics..cctor: rule 1, names Core outside a boundary: "
                + "SlideRule.Rendering.PathFormat.Relative",
                "Probe.Statics..cctor: rule 4, the fault path names Core: "
                + "SlideRule.Rendering.PathFormat.Relative");
        CoreFirewall.GuardedReferences(image)
            .ShouldBe(["SlideRule"]);
    }

    // The probe compiled against the running host's own assemblies, which hold the real Core, and emitted to
    // memory: the walk reads bytes, so no file is written. It is named as the build analyzer is, because it stands
    // in for an analyzer assembly, and an analyzer reads Core's engine through the InternalsVisibleTo entry Core
    // declares for that name.
    private static ImmutableArray<byte> Emitted(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14), "Probe.cs");
        var compilation = CSharpCompilation.Create(
            "SlideRule.Analyzers", [tree], TrustedPlatform.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        EmitResult result = compilation.Emit(stream);
        List<string> errors = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();
        errors.ShouldBeEmpty("The probe has to compile before the walk over it means anything.");

        return ImmutableArray.Create(stream.ToArray());
    }
}
