using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SlideRule.Analyzers;

/// <summary>
///     The build analyzer's own descriptors, and the policy that hands them to the engine.
/// </summary>
/// <remarks>
///     <para>
///         The missing model file, the unchecked rule and the fault are reported from a compilation end action
///         and carry the <c>CompilationEnd</c> tag, which is what lets a host run them at the right point. The
///         two rule warnings are reported from a per-document action and <see cref="UnreadableFile" /> from an
///         additional-file action, and none of those is tagged, because the tag would be a claim about where
///         the warning comes from that is not true. A project that sets <c>SlideRuleTier</c> to <c>build</c>
///         gets the rule warnings from an end action instead, under the same untagged descriptors.
///     </para>
///     <para>
///         The fault's ID carries two descriptors: the fault itself, and <see cref="VersionMismatch" />, the
///         analyzer finding itself handed another version of SlideRule than its own. The second has
///         a message of its own because the fault's asks the reader to report a bug, which is the wrong
///         advice for two packages kept at two versions; it keeps the ID so that a build excluding the fault
///         by name excludes this too.
///     </para>
///     <para>
///         An analyzer that hosts the engine by compiling its sources in leaves this file out, so it declares
///         none of these descriptors. Nothing here names a type of SlideRule or SlideRule.Extraction, so
///         loading this type binds neither and the fault can always be reported.
///     </para>
/// </remarks>
internal static class BuildTierDescriptors
{
    private const string Category = "SlideRule";

    private const string HelpLink =
        "https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md#diagnostics";

    /// <summary>A rule that is law was broken by code in the project being built.</summary>
    internal static readonly DiagnosticDescriptor BrokenRule = new(
        "SR2001",
        "An architecture rule is broken",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "Code in this project breaks a rule of the architecture spec. The message carries the rule's ID, its "
        + "sentence, the reason it exists, how to fix the violation and, where the rule names one, the type to "
        + "copy. 'sliderule check' gives the whole-solution verdict and stays the gate; this warning is the "
        + "same finding, reported while the project builds.",
        HelpLink);

    /// <summary>A Migrate rule found debt its captured baseline does not grandfather.</summary>
    internal static readonly DiagnosticDescriptor NewDebt = new(
        "SR2002",
        "A Migrate rule has a violation its baseline does not hold",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "Code in this project adds a violation that a Migrate rule's baseline does not hold, or adds sites to "
        + "a pair it does. Violations the baseline already holds are not reported.",
        HelpLink);

    /// <summary>The build handed the analyzer no model file, so it checked nothing.</summary>
    internal static readonly DiagnosticDescriptor NoModelFile = new(
        "SR2090",
        "No SlideRule model file was found",
        "No sliderule.model.json was found above this project, so this build checked no architecture rule. "
        + "Run 'sliderule render --model' in the solution directory and commit the file it writes.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "The analyzer reads the architecture from sliderule.model.json, which 'sliderule render --model' "
        + "writes beside the solution. Without it the build checks nothing.",
        HelpLink,
        WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>A model or baseline file the build handed over could not be read.</summary>
    internal static readonly DiagnosticDescriptor UnreadableFile = new(
        "SR2091",
        "A SlideRule file could not be read",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A model file or a baseline file the build handed to the analyzer is malformed, fails its integrity "
        + "check, or was written in a version this analyzer does not read. The warning sits in that file at "
        + "the place the read stopped.",
        HelpLink);

    /// <summary>A ratcheted rule was left out because this build was given no baseline for it.</summary>
    internal static readonly DiagnosticDescriptor UncheckedRule = new(
        "SR2092",
        "A ratcheted rule was not checked because the build was given no baseline for it",
        "Rule '{0}' was not checked in this build. It is ratcheted and the build was given no captured "
        + "baseline for it, so checking it here would report its existing violations as new. If its baseline "
        + "file is kept outside arch/baselines/, add that file to the project as a SlideRuleBaseline item; "
        + "if the rule has no captured baseline yet, 'sliderule baseline init' captures one.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A ratcheted rule is checked in the build only against its captured baseline. Without one, the build "
        + "would report everything the baseline exists to grandfather, so the rule is left to 'sliderule "
        + "check'. The warning appears only where the rule would have reported in this project.",
        HelpLink,
        WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>The analyzer itself failed, which is a fault in the analyzer and not a finding about code.</summary>
    internal static readonly DiagnosticDescriptor AnalyzerFault = new(
        "SR2093",
        "The SlideRule analyzer failed",
        "The SlideRule analyzer failed in this project, so some or all of its findings are missing: {0}: "
        + "{1}. This is a fault in the analyzer and says nothing about the code, and 'sliderule check' is "
        + "unaffected. Report it at https://github.com/andypgray/sliderule/issues.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "An internal fault stopped the analyzer in this project. It is reported as a warning of its own so "
        + "that a build treating warnings as errors can exclude it by ID.",
        HelpLink,
        WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    ///     The compiler handed the analyzer another version of SlideRule than its own, so it checked nothing.
    /// </summary>
    internal static readonly DiagnosticDescriptor VersionMismatch = new(
        "SR2093",
        "The SlideRule analyzer failed",
        "The SlideRule analyzer checked nothing in this project: it is version {0}, and the compiler loaded "
        + "version {1} of SlideRule for it. That happens when a project references SlideRule and "
        + "SlideRule.Analyzers at two different versions, so keep the two packages on one version. "
        + "'sliderule check' is unaffected.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "The analyzer was built as one version and the compiler loaded another version of SlideRule "
        + "for it, so it checked nothing in this project. It is reported under the fault's ID so that a build "
        + "treating warnings as errors can exclude both by that one ID.",
        HelpLink,
        WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>Every descriptor this analyzer can report, which is what it declares it supports.</summary>
    internal static ImmutableArray<DiagnosticDescriptor> All { get; } = ImmutableArray.Create(
        BrokenRule, NewDebt, NoModelFile, UnreadableFile, UncheckedRule, AnalyzerFault, VersionMismatch);

    /// <summary>
    ///     The build analyzer's policy: the model file the build hands over, a descriptor by posture, and the
    ///     tier read from <c>SlideRuleTier</c>.
    /// </summary>
    internal static BuildTierPolicy ProductPolicy { get; } = BuildTierPolicy.Handing(
        new BuildTierPolicy.HandedFiles(
            tierProperty: "build_property.SlideRuleTier",
            brokenRule: BrokenRule,
            newDebt: NewDebt,
            noModelFile: NoModelFile,
            unreadableFile: UnreadableFile,
            uncheckedRule: UncheckedRule,
            versionMismatch: VersionMismatch),
        AnalyzerFault);
}
