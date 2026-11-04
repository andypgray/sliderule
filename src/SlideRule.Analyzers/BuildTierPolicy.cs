using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SlideRule.Analyzers;

/// <summary>
///     What an analyzer hosting the <see cref="BuildTierEngine" /> decides for it: where the model comes from,
///     which descriptor each diagnostic takes, and which build property, if any, switches the tier.
/// </summary>
/// <remarks>
///     Immutable, and shared by every compilation the hosting analyzer sees. Nothing here names a type of
///     SlideRule or SlideRule.Extraction, so a field of this type binds neither when the analyzer loads. Every
///     descriptor the engine reports under comes from here, so a host can report only what it hands over.
/// </remarks>
internal sealed class BuildTierPolicy
{
    private readonly IReadOnlyDictionary<string, DiagnosticDescriptor>? _ruleDescriptors;

    private BuildTierPolicy(
        HandedFiles? handed, SourceText? suppliedModel,
        IReadOnlyDictionary<string, DiagnosticDescriptor>? ruleDescriptors, DiagnosticDescriptor faultDescriptor)
    {
        Handed = handed;
        SuppliedModel = suppliedModel;
        _ruleDescriptors = ruleDescriptors;
        FaultDescriptor = faultDescriptor;
    }

    /// <summary>
    ///     What a host reading the files the build hands over needs, or null for a host that carries its model.
    /// </summary>
    internal HandedFiles? Handed { get; }

    /// <summary>
    ///     The model the analyzer carries, or null where the build hands one over as an additional file.
    /// </summary>
    internal SourceText? SuppliedModel { get; }

    /// <summary>The descriptor a fault in the analyzer is reported under.</summary>
    internal DiagnosticDescriptor FaultDescriptor { get; }

    /// <summary>
    ///     A host reading the model file and baselines the build hands over: a finding reports under
    ///     <paramref name="handed" />'s descriptor for its posture, and a fault under
    ///     <paramref name="faultDescriptor" />.
    /// </summary>
    internal static BuildTierPolicy Handing(HandedFiles handed, DiagnosticDescriptor faultDescriptor)
    {
        return new BuildTierPolicy(handed, null, null, faultDescriptor);
    }

    /// <summary>
    ///     A host carrying <paramref name="model" />: each rule reports under its own descriptor from
    ///     <paramref name="ruleDescriptors" />, a fault under <paramref name="faultDescriptor" />, and there is no
    ///     tier switch. The version check passes by construction, because the engine and the model's reader
    ///     travel in one assembly.
    /// </summary>
    internal static BuildTierPolicy Supplied(
        SourceText model, IReadOnlyDictionary<string, DiagnosticDescriptor> ruleDescriptors,
        DiagnosticDescriptor faultDescriptor)
    {
        return new BuildTierPolicy(null, model, ruleDescriptors, faultDescriptor);
    }

    /// <summary>
    ///     The descriptor a finding of <paramref name="ruleId" /> takes: the rule's own where the host keeps one
    ///     per rule, and otherwise the handed files' one for new debt or for a broken rule.
    /// </summary>
    internal DiagnosticDescriptor DescriptorFor(string ruleId, bool newDebt)
    {
        if (_ruleDescriptors is not null) return _ruleDescriptors[ruleId];

        // Each factory sets exactly one of the two, so a policy with no per-rule table hands files.
        HandedFiles handed = Handed!;

        return newDebt ? handed.NewDebt : handed.BrokenRule;
    }

    /// <summary>
    ///     The IDs of the rules whose own descriptor is switched off in every syntax tree of
    ///     <paramref name="compilation" />, so the compiler would discard each of their findings wherever it was
    ///     reported; empty for a host that keeps no descriptor per rule.
    /// </summary>
    internal IReadOnlyList<string> SwitchedOff(
        Compilation compilation, AnalyzerOptions options, CancellationToken cancellationToken)
    {
        if (_ruleDescriptors is null) return Array.Empty<string>();

        var off = new List<string>();
        foreach (KeyValuePair<string, DiagnosticDescriptor> rule in _ruleDescriptors)
            if (IsOffInEveryTree(rule.Value, compilation, options, cancellationToken))
                off.Add(rule.Key);

        return off;
    }

    // Off only where every setting that answers for a tree says none, or none answers and the descriptor ships
    // off. The compiler ranks conflicting settings in an order of its own, so a setting that enables the
    // descriptor anywhere keeps the rule whatever it conflicts with: a rule checked for nothing costs time, and
    // one wrongly declined silences a warning. The bulk keys answer only where the compiler lets them, for a
    // descriptor enabled by default that no setting names, and any value but none keeps the rule.
    private static bool IsOffInEveryTree(
        DiagnosticDescriptor descriptor, Compilation compilation, AnalyzerOptions options,
        CancellationToken cancellationToken)
    {
        foreach (string tag in descriptor.CustomTags)
            if (tag is WellKnownDiagnosticTags.NotConfigurable or CustomSeverityConfigurableTag)
                return false;

        CompilationOptions compilationOptions = compilation.Options;
        SyntaxTreeOptionsProvider? treeOptions = compilationOptions.SyntaxTreeOptionsProvider;

        var named = false;
        if (compilationOptions.SpecificDiagnosticOptions.TryGetValue(descriptor.Id, out ReportDiagnostic specific))
        {
            if (Enables(descriptor, specific)) return false;

            named = true;
        }

        if (treeOptions is not null
            && treeOptions.TryGetGlobalDiagnosticValue(descriptor.Id, cancellationToken, out ReportDiagnostic global))
        {
            if (Enables(descriptor, global)) return false;

            named = true;
        }

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            bool namedHere = named;
            if (treeOptions is not null
                && treeOptions.TryGetDiagnosticValue(tree, descriptor.Id, cancellationToken, out ReportDiagnostic own))
            {
                if (Enables(descriptor, own)) return false;

                namedHere = true;
            }

            if (namedHere || !descriptor.IsEnabledByDefault) continue;

            AnalyzerConfigOptions treeConfig = options.AnalyzerConfigOptionsProvider.GetOptions(tree);
            if (!treeConfig.TryGetValue(CategorySeverityKey(descriptor.Category), out string? bulk)
                && !treeConfig.TryGetValue(BulkSeverityKey, out bulk))
                return false;

            // The compiler reads a severity value without regard to case.
            if (!string.Equals(bulk, "none", StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    // Default is the descriptor's own enablement, which is how the compiler reads it.
    private static bool Enables(DiagnosticDescriptor descriptor, ReportDiagnostic value)
    {
        return value switch
        {
            ReportDiagnostic.Suppress => false,
            ReportDiagnostic.Default => descriptor.IsEnabledByDefault,
            _ => true
        };
    }

    private static string CategorySeverityKey(string category)
    {
        return "dotnet_analyzer_diagnostic.category-" + category + ".severity";
    }

    private const string BulkSeverityKey = "dotnet_analyzer_diagnostic.severity";

    // The tag a descriptor carries when its analyzer reads its severity from settings of its own, which the compiler
    // never reads for it; the constant is missing from the compiler API this assembly builds against.
    private const string CustomSeverityConfigurableTag = "CustomSeverityConfigurable";

    /// <summary>
    ///     The tier switch and the descriptors that only a host reading the files the build hands over can raise.
    /// </summary>
    internal sealed class HandedFiles(
        string tierProperty,
        DiagnosticDescriptor brokenRule,
        DiagnosticDescriptor newDebt,
        DiagnosticDescriptor noModelFile,
        DiagnosticDescriptor unreadableFile,
        DiagnosticDescriptor uncheckedRule,
        DiagnosticDescriptor versionMismatch)
    {
        /// <summary>The global analyzer-config key the tier is read from.</summary>
        internal string TierProperty { get; } = tierProperty;

        /// <summary>A finding of a rule that is law.</summary>
        internal DiagnosticDescriptor BrokenRule { get; } = brokenRule;

        /// <summary>A finding a ratcheted rule's baseline does not hold.</summary>
        internal DiagnosticDescriptor NewDebt { get; } = newDebt;

        /// <summary>A build that handed over no model file.</summary>
        internal DiagnosticDescriptor NoModelFile { get; } = noModelFile;

        /// <summary>A model or baseline file that would not read.</summary>
        internal DiagnosticDescriptor UnreadableFile { get; } = unreadableFile;

        /// <summary>A ratcheted rule left unchecked for want of a captured baseline.</summary>
        internal DiagnosticDescriptor UncheckedRule { get; } = uncheckedRule;

        /// <summary>An analyzer bound to another version of SlideRule than its own.</summary>
        internal DiagnosticDescriptor VersionMismatch { get; } = versionMismatch;
    }
}
