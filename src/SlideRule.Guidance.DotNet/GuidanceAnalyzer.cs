using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Analyzers;

namespace SlideRule.Guidance.DotNet;

/// <summary>
///     Checks the project being compiled against the canonical .NET guidance this assembly carries as a model,
///     and reports a broken rule under the rule's own ID at the offending code.
/// </summary>
/// <remarks>
///     The work is the build analyzer's engine, run under <see cref="BuildTierPolicy.Supplied" />: the model
///     governs any compilation, whatever its assembly is called, and a rule the project's own
///     <c>sliderule.model.json</c> declares is left to that model, so the project's posture, baseline and
///     exemptions for it hold and the rule never reports twice.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class GuidanceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The manifest name of the model this assembly embeds.</summary>
    internal const string ModelResource = "SlideRule.Guidance.DotNet.guidance.model.json";

    private static readonly BuildTierPolicy Policy = BuildTierPolicy.Supplied(
        EmbeddedModel(), GuidanceDiagnostics.ByRule, GuidanceDiagnostics.AnalyzerFault);

    private readonly BuildTierEngine _engine;

    /// <summary>The constructor the compiler calls, which arms no seam.</summary>
    public GuidanceAnalyzer()
        : this(null)
    {
    }

    /// <summary>
    ///     The same analyzer with a test seam run inside the guard of whichever action checks, before any
    ///     extraction; null in production.
    /// </summary>
    internal GuidanceAnalyzer(Action? beforeCheck)
    {
        _engine = new BuildTierEngine(Policy, beforeCheck);
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => GuidanceDiagnostics.All;

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        // Generated code is analysed and reported on, as the build analyzer does it, so the two agree with the
        // whole-solution check about where a rule is broken.
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        _engine.Register(context);
    }

    internal static SourceText EmbeddedModel()
    {
        using Stream stream = typeof(GuidanceAnalyzer).Assembly.GetManifestResourceStream(ModelResource)
                              ?? throw new InvalidOperationException($"The resource '{ModelResource}' is missing.");

        return SourceText.From(stream, Encoding.UTF8);
    }
}
