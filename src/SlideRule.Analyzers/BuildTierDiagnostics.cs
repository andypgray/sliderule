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
///     The diagnostics the build-tier engine reports, each under the descriptor its policy supplies, and the
///     line each one carries.
/// </summary>
/// <remarks>
///     The factories that take a Core type are boundaries, never inlined into a caller, and every caller is
///     another boundary. The fault's two factories name no Core type, so a copy of SlideRule that will not
///     bind cannot stop the fault itself being reported.
/// </remarks>
internal static class BuildTierDiagnostics
{
    /// <summary>
    ///     One finding as the diagnostic it prints, under the descriptor <paramref name="policy" /> gives it, at
    ///     <paramref name="location" />, carrying the rule, the violation kind and the site as properties a
    ///     fixer or a report can read without re-parsing the message.
    /// </summary>
    /// <remarks>
    ///     An absent value is an absent key, never a null one: a reader asking for <c>subject</c> on an edge
    ///     finding gets "no such key" rather than a key whose value it must then test.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Diagnostic Finding(BuildFinding finding, Location location, BuildTierPolicy policy)
    {
        DiagnosticDescriptor descriptor =
            policy.DescriptorFor(finding.RuleId, finding.Kind == BuildFindingKind.NewDebt);

        return Diagnostic.Create(descriptor, location, PropertiesOf(finding), finding.Message);
    }

    /// <summary>The warning for a build that was handed no model file it could read.</summary>
    internal static Diagnostic MissingModelFile(DiagnosticDescriptor descriptor)
    {
        return Diagnostic.Create(descriptor, Location.None);
    }

    /// <summary>
    ///     The warning for a model file that would not read, placed in that file at the position the read
    ///     stopped, with the recovery each refusal kind calls for.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Diagnostic ModelRefused(
        ModelRefusal refusal, string path, SourceText text, DiagnosticDescriptor descriptor)
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

        return Diagnostic.Create(descriptor, At(path, text, refusal.Position), line);
    }

    /// <summary>
    ///     The warning for a baseline file that would not read, placed in that file at the position the read
    ///     stopped. One wording for every refusal kind: the tool states the recovery, and this says so.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Diagnostic BaselineRefused(
        BaselineRefusal refusal, string path, SourceText text, DiagnosticDescriptor descriptor)
    {
        string line = "This baseline file could not be read, so this build did not check the rules that use "
                      + "it: " + refusal.Detail
                      + " 'sliderule check' reports the same refusal with its recovery steps.";

        return Diagnostic.Create(descriptor, At(path, text, refusal.Position), line);
    }

    /// <summary>
    ///     The warning naming a ratcheted rule this build left out for want of a captured baseline. It carries
    ///     the rule's ID as its <c>rule</c> property, so a reader need not parse the message for it.
    /// </summary>
    internal static Diagnostic RuleNotChecked(string ruleId, DiagnosticDescriptor descriptor)
    {
        ImmutableDictionary<string, string?> properties =
            ImmutableDictionary<string, string?>.Empty.Add("rule", ruleId);

        return Diagnostic.Create(descriptor, Location.None, properties, ruleId);
    }

    /// <summary>
    ///     The warning an analyzer fault becomes under <paramref name="descriptor" />: the exception's type name
    ///     and the first line of its message, so a reader can tell two faults apart without a stack trace in a
    ///     build log.
    /// </summary>
    internal static Diagnostic AnalyzerFailed(Exception exception, DiagnosticDescriptor descriptor)
    {
        return Diagnostic.Create(descriptor, Location.None, exception.GetType().Name, Opening(exception.Message));
    }

    /// <summary>
    ///     The warning for an analyzer handed another version of SlideRule than its own, naming both
    ///     versions, the analyzer's first.
    /// </summary>
    internal static Diagnostic VersionsMismatched(
        string analyzerVersion, string coreVersion, DiagnosticDescriptor descriptor)
    {
        return Diagnostic.Create(descriptor, Location.None, analyzerVersion, coreVersion);
    }

    // The first line alone, because a build surface shows a warning on one row; the trailing full stop comes
    // off because the message format puts one after it. Must agree with SpecLiteralDiagnostics' copy, so both
    // analyzers word a fault alike; CopiedMemberParityTests holds them identical.
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
