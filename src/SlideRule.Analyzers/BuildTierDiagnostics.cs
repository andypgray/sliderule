using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Baselines;
using SlideRule.Internal;
using SlideRule.ModelFile;
using SlideRule.Rendering;

namespace SlideRule.Analyzers;

/// <summary>
///     The warnings the build tier reports, and the line each one carries.
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
///         The factories that take a Core type are boundaries, never inlined into a caller, and every caller
///         is another boundary. The fault's two factories and everything the descriptors are built from name
///         no Core type, so a copy of SlideRule that will not bind cannot stop the fault itself being
///         reported.
///     </para>
/// </remarks>
internal static class BuildTierDiagnostics
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
    ///     One finding as the warning it prints, at <paramref name="location" />, carrying the rule, the
    ///     violation kind and the site as properties a fixer or a report can read without re-parsing the
    ///     message.
    /// </summary>
    /// <remarks>
    ///     An absent value is an absent key, never a null one: a reader asking for <c>subject</c> on an edge
    ///     finding gets "no such key" rather than a key whose value it must then test.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Diagnostic Finding(BuildFinding finding, Location location)
    {
        DiagnosticDescriptor descriptor = finding.Kind == BuildFindingKind.NewDebt ? NewDebt : BrokenRule;

        return Diagnostic.Create(descriptor, location, PropertiesOf(finding), finding.Message);
    }

    /// <summary>The warning for a build that was handed no model file it could read.</summary>
    internal static Diagnostic MissingModelFile()
    {
        return Diagnostic.Create(NoModelFile, Location.None);
    }

    /// <summary>
    ///     The warning for a model file that would not read, placed in that file at the position the read
    ///     stopped, with the recovery each refusal kind calls for.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Diagnostic ModelRefused(ModelRefusal refusal, string path, SourceText text)
    {
        string recovery = refusal.Kind switch
        {
            ModelRefusalKind.Newer =>
                "Update the SlideRule.Analyzers package to the version of the tool that wrote it.",
            ModelRefusalKind.Older => "Run 'sliderule render --model' with the current tool to rewrite it.",
            _ => "Run 'sliderule render --model' to rewrite it."
        };
        string line = "This model file could not be read, so this build checked no architecture rule: "
                      + refusal.Message + " " + recovery;

        return Diagnostic.Create(UnreadableFile, At(path, text, refusal.Position), line);
    }

    /// <summary>
    ///     The warning for a baseline file that would not read, placed in that file at the position the read
    ///     stopped. One wording for every refusal kind: the tool states the recovery, and this says so.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Diagnostic BaselineRefused(BaselineRefusal refusal, string path, SourceText text)
    {
        string line = "This baseline file could not be read, so this build did not check the rules that use "
                      + "it: " + refusal.Detail
                      + " 'sliderule check' reports the same refusal with its recovery steps.";

        return Diagnostic.Create(UnreadableFile, At(path, text, refusal.Position), line);
    }

    /// <summary>
    ///     The warning naming a ratcheted rule this build left out for want of a captured baseline. It carries
    ///     the rule's ID as its <c>rule</c> property, so a reader need not parse the message for it.
    /// </summary>
    internal static Diagnostic RuleNotChecked(string ruleId)
    {
        ImmutableDictionary<string, string?> properties =
            ImmutableDictionary<string, string?>.Empty.Add("rule", ruleId);

        return Diagnostic.Create(UncheckedRule, Location.None, properties, ruleId);
    }

    /// <summary>
    ///     The warning an analyzer fault becomes: the exception's type name and the first line of its
    ///     message, so a reader can tell two faults apart without a stack trace in a build log.
    /// </summary>
    internal static Diagnostic AnalyzerFailed(Exception exception)
    {
        return Diagnostic.Create(AnalyzerFault, Location.None, exception.GetType().Name, Opening(exception.Message));
    }

    /// <summary>
    ///     The warning for an analyzer handed another version of SlideRule than its own, naming both
    ///     versions, the analyzer's first.
    /// </summary>
    internal static Diagnostic VersionsMismatched(string analyzerVersion, string coreVersion)
    {
        return Diagnostic.Create(VersionMismatch, Location.None, analyzerVersion, coreVersion);
    }

    // The first line alone, because a build surface shows a warning on one row; the trailing full stop comes
    // off because the message format puts one after it.
    private static string Opening(string message)
    {
        int lineBreak = message.IndexOf('\n');
        string first = lineBreak < 0 ? message : message.Substring(0, lineBreak);
        string trimmed = first.Trim();
        bool ended = trimmed.Length > 0 && trimmed[trimmed.Length - 1] == '.';

        return ended ? trimmed.Substring(0, trimmed.Length - 1) : trimmed;
    }

    // A refusal's line and column are 1-based and a LinePosition is 0-based; the span is empty and clamped
    // inside the text, because a position past the end is a location a host refuses to render.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Location At(string path, SourceText text, JsonPosition at)
    {
        int start = at.Offset < 0 ? 0 : at.Offset;
        if (start > text.Length) start = text.Length;

        int row = at.Line > 0 ? at.Line - 1 : 0;
        int character = at.Column > 0 ? at.Column - 1 : 0;
        var position = new LinePosition(row, character);

        return Location.Create(path, new TextSpan(start, 0), new LinePositionSpan(position, position));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ImmutableDictionary<string, string?> PropertiesOf(BuildFinding finding)
    {
        ImmutableDictionary<string, string?>.Builder properties =
            ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        properties.Add("rule", finding.RuleId);
        properties.Add("kind", finding.ViolationKind.ToString());
        if (finding.Source is { } source) properties.Add("source", source);
        if (finding.Target is { } target) properties.Add("target", target);
        if (finding.Subject is { } subject) properties.Add("subject", subject);
        properties.Add("file", finding.FilePath);
        properties.Add("line", finding.Line.ToString(CultureInfo.InvariantCulture));

        return properties.ToImmutable();
    }
}
