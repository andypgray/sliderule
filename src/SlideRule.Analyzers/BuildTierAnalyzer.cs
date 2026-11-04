using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SlideRule.Analyzers;

/// <summary>
///     Checks the project being compiled against the architecture the build handed over as
///     <c>sliderule.model.json</c>, and reports a broken rule as a warning at the offending code, from
///     the document being analysed and at the end of the build.
/// </summary>
/// <remarks>
///     The whole of the work is the <see cref="BuildTierEngine" />'s, run under
///     <see cref="BuildTierDescriptors.ProductPolicy" />. This class is what the compiler finds and creates.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class BuildTierAnalyzer : DiagnosticAnalyzer
{
    private readonly BuildTierEngine _engine;

    /// <summary>The constructor the compiler calls, which arms no seam.</summary>
    public BuildTierAnalyzer()
        : this(beforeCheck: null)
    {
    }

    /// <summary>
    ///     The same analyzer with the engine's test seams armed; each is described on
    ///     <see cref="BuildTierEngine" />'s constructor, and every one is null in production.
    /// </summary>
    internal BuildTierAnalyzer(
        Action? beforeCheck = null,
        Action? beforeStart = null,
        Action? beforeModelRead = null,
        Action? beforeBaselineRead = null,
        Action? beforeModelRefusal = null,
        Action? beforeBaselineRefusal = null,
        Func<(string? Analyzer, string? Core)>? versions = null)
    {
        _engine = new BuildTierEngine(
            BuildTierDescriptors.ProductPolicy, beforeCheck, beforeStart, beforeModelRead, beforeBaselineRead,
            beforeModelRefusal, beforeBaselineRefusal, versions);
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => BuildTierDescriptors.All;

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        // Generated code is analysed and reported on, because the whole-solution check reports a site in a
        // generated file and the two must not disagree about where a rule is broken.
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        _engine.Register(context);
    }
}
