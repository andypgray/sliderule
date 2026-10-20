using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace SlideRule.SpecAnalyzers;

/// <summary>
///     The warnings the spec-literal analyzer reports: the verdicts a string literal draws where it is
///     written, and the analyzer's own fault, which comes in two forms.
/// </summary>
/// <remarks>
///     <para>
///         Every verdict carries <c>{0}</c> as its whole message: the sentence is decided in the contract, by
///         the same types the spec loader decides with, so the warning at the literal and the error at load
///         cannot drift into different English.
///     </para>
///     <para>
///         Each descriptor is written out, never generated from the code enum: the enum is public contract
///         surface and grows by appending, so a newer contract beside an older analyzer can hand back a code
///         that has no wording here. <see cref="For" /> answers null for it and the caller stays silent, the
///         safe direction, because loading the spec still reports every one of them.
///     </para>
///     <para>
///         <c>CompilationEnd</c> is a claim that a host must run a compilation end action to get the warning,
///         and no verdict needs one: a literal is decided where it is written and a collision when the method
///         body it sits in ends, both of which a live editor reaches, and the tag would cost the squiggle while
///         the author is typing. Only <see cref="VersionMismatch" /> carries it, because a version difference is
///         a fact about the project rather than about a literal, and while it holds the analyzer reads no
///         literal at all.
///     </para>
///     <para>
///         The fault's ID carries two descriptors, the fault and the version difference, which has a message
///         of its own because the fault's asks the reader to report a bug. Nothing here takes a contract type:
///         a code arrives as its number and its name, so every factory, the fault path included, is safe to
///         call whatever copy of the contract the compiler loaded.
///     </para>
/// </remarks>
internal static class SpecLiteralDiagnostics
{
    private const string Category = "SlideRule";

    private const string HelpLink =
        "https://github.com/andypgray/sliderule/blob/main/src/SlideRule/README.md#diagnostics";

    private const string IdPrefix = "SR";

    /// <summary>
    ///     What a verdict's code is added to for its ID. The tool's own numbers start at
    ///     <c>SR1090</c>, so the contract's codes must stay below ninety for the two ranges not to meet;
    ///     <c>SpecLiteralDescriptorTests</c> holds them there.
    /// </summary>
    private const int CodeOffset = 1000;

    /// <summary>
    ///     The closing sentence every verdict description ends with, written once so that the descriptions
    ///     cannot come to say it in different ways.
    /// </summary>
    private const string SameVerdictAtLoad =
        "Loading the spec reports the same mistake as a validation error, from 'sliderule check', "
        + "'sliderule render' and the xUnit adapter alike; this warning is that verdict, reported where "
        + "the literal is written.";

    /// <summary>Two declarations in one method body produce the same post-desugar rule ID.</summary>
    internal static readonly DiagnosticDescriptor DuplicateId = new(
        "SR1000",
        "A rule or scope ID is declared twice",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "Two declarations in this method produce the same rule ID. IDs are unique across the whole spec, "
        + "counting the two rules every scope contributes of its own, '{scope-id}/containment' and "
        + "'{scope-id}/tripwire', so two scopes declared with one ID are reported under those two IDs, "
        + "which appear nowhere in the source. The warning sits on the later declaration and gives the "
        + "first as a related location. Only declarations inside one method body are compared here; a "
        + "duplicate across methods, files or a rule pack is reported when the spec is loaded. Rename one "
        + "of them. " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A rule ID falls inside the namespace a scope reserves for its own two rules.</summary>
    internal static readonly DiagnosticDescriptor IdExtendsScope = new(
        "SR1001",
        "A rule ID sits under a scope's reserved prefix",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A rule's ID equals a scope's ID, or begins with it and a slash. A scope reserves '{scope-id}/' "
        + "for the rules it contributes, 'containment' and 'tripwire'. Give the rule an ID outside that "
        + "prefix. Only a rule and a scope declared inside one method body are compared here. "
        + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A prose argument was left empty or whitespace.</summary>
    internal static readonly DiagnosticDescriptor BlankProse = new(
        "SR1005",
        "A prose argument is blank",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A prose argument is empty or only whitespace: a Because, a Fix, a Citation, a migration's 'from' "
        + "description, a Baseline path, a scope's Dragons or DragonsDoc, a layer's Purpose, or the "
        + "description a Where or a custom Must requires. Write the line, or drop the call. "
        + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A prose argument carries a line break.</summary>
    internal static readonly DiagnosticDescriptor MultiLineProse = new(
        "SR1006",
        "A prose argument spans more than one line",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A prose argument carries a line break. Every prose value is a single line; link a longer document "
        + "with a scope's DragonsDoc instead. " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A rule or scope ID does not match the ID grammar.</summary>
    internal static readonly DiagnosticDescriptor MalformedId = new(
        "SR1008",
        "A rule or scope ID is malformed",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A rule or scope ID must be lowercase letters, digits and hyphens, in segments joined by '/': it "
        + "has to match ^[a-z0-9-]+(/[a-z0-9-]+)*$. The convention is 'area/rule-name'. "
        + SameVerdictAtLoad,
        HelpLink);

    /// <summary>Two layers in one method body were given the same name.</summary>
    internal static readonly DiagnosticDescriptor DuplicateLayerName = new(
        "SR1010",
        "A layer name is declared twice",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "Two arch.Layer calls in this method give the same name. Layer names are unique within a spec, "
        + "because the name is how every rule, report and generated context refers to the layer. The "
        + "warning sits on the later declaration and gives the first as a related location. Only layers "
        + "declared inside one method body are compared here. " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A named member was given no name.</summary>
    internal static readonly DiagnosticDescriptor BlankMemberName = new(
        "SR1012",
        "A member name is blank",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "An arch.Member(typeof(T), name) was given an empty member name. Pass the name the type declares, "
        + "ideally as nameof(...). " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A glob, an exact name or an affix was left empty or whitespace.</summary>
    internal static readonly DiagnosticDescriptor BlankPattern = new(
        "SR1016",
        "A pattern, name or affix is blank",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A glob, name or affix is empty or only whitespace: a namespace glob, a type-name glob, an exact "
        + "name on Named, a project name on arch.Project or MustResideInProject, a suffix or prefix, a "
        + "MustHaveExactlyOneCounterpart name template, or a string-named attribute, interface, base type, "
        + "return type or parameter type. A blank affix is true of every name and a blank glob fails when "
        + "the spec is checked, so neither is likely to be what was meant. " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A namespace glob strands a wildcard behind a trailing subtree operator.</summary>
    internal static readonly DiagnosticDescriptor UnanchoredSubtreePattern = new(
        "SR1017",
        "A namespace pattern can never match",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A namespace glob ends in '.*' and the text before that ending also contains a '*', such as "
        + "'MyApp.*.Controllers.*'. Everything before a trailing '.*' is matched literally, so such a glob "
        + "matches nothing. Anchor the subtree on a literal prefix ('MyApp.Web.Controllers.*'), or drop "
        + "the trailing '.*' and let the interior '*' match one segment. " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A project name or glob on the project stratum was left empty.</summary>
    internal static readonly DiagnosticDescriptor BlankProjectPattern = new(
        "SR1024",
        "A project name or pattern is blank",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "An arch.Projects.Named was given an empty project name, or an arch.Projects.Matching an empty "
        + "glob. A blank name matches no project and a blank glob matches every one, so neither is likely "
        + "to be what was meant. " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A permitted target framework was left empty.</summary>
    internal static readonly DiagnosticDescriptor BlankTargetFramework = new(
        "SR1025",
        "A target framework is blank",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A MustOnlyTarget or MustTarget was given an empty target framework. A blank moniker matches "
        + "nothing. On MustOnlyTarget it narrows the permitted list instead of widening it, so the rule stays "
        + "green until some project targets the framework that was meant to be allowed; on MustTarget it "
        + "requires a framework no project targets, so the rule fails every project it reads. "
        + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A counterpart name template derives one fixed name for every subject.</summary>
    internal static readonly DiagnosticDescriptor CounterpartTemplateWithoutPlaceholder = new(
        "SR1026",
        "A counterpart name template has no {Name} placeholder",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A MustHaveExactlyOneCounterpart name template carries no '{Name}' placeholder, such as "
        + "'IService'. Every subject would derive the same fixed name, which makes the rule a claim about "
        + "how many types carry that one name and says nothing about correspondence. Write a template such "
        + "as 'I{Name}'. Substitution is case-sensitive, so '{name}' is the usual cause. "
        + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A citation is not something every consuming surface can render as a link.</summary>
    internal static readonly DiagnosticDescriptor MalformedCitation = new(
        "SR1030",
        "A citation is not an absolute http(s) URL",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A Citation is not an absolute http or https URL: a pasted page title, a repository-relative path, "
        + "or a URI on another scheme. Every surface renders the value as a link and hands it on unread "
        + "(the generated agent context autolinks it, and SARIF output publishes it as the rule's help "
        + "URI), so nothing downstream could report a bad one. " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A layer was declared with no name.</summary>
    internal static readonly DiagnosticDescriptor BlankLayerName = new(
        "SR1031",
        "A layer name is blank",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "An arch.Layer was given a name that is empty or only whitespace. The name is how every rule, "
        + "report and generated context refers to the layer, and how a second layer is told from it. Name "
        + "the layer in the first argument. " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A package pattern was left empty.</summary>
    internal static readonly DiagnosticDescriptor BlankPackagePattern = new(
        "SR1032",
        "A package pattern is blank",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A MustOnlyReferencePackages or MustNotReferencePackages was given an empty package pattern. No package "
        + "has an empty identifier, so a blank pattern matches nothing. On MustOnlyReferencePackages it permits "
        + "nothing, so the rule fails on the package that was meant to be allowed; on MustNotReferencePackages it "
        + "forbids nothing, so the rule stays green whatever the projects reference. " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A property name or value was left empty.</summary>
    internal static readonly DiagnosticDescriptor BlankProperty = new(
        "SR1033",
        "A property name or value is blank",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "A MustSetProperty was given an empty property name or value. No property has an empty name, and MSBuild "
        + "treats a property set to nothing as unset, so either way the rule asks for something no project can "
        + "declare and fails every project it reads. " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>A path glob was written in a shape no declaring file can match.</summary>
    internal static readonly DiagnosticDescriptor MalformedPathGlob = new(
        "SR1035",
        "A path glob is malformed",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "An Under was given a path glob that is rooted, contains a backslash, or has a `.`, `..` or empty segment or "
        + "a `**` inside a longer segment. A path glob is relative to the solution directory with `/` between "
        + "segments, so a glob in any of those shapes matches no declaring file. " + SameVerdictAtLoad,
        HelpLink);

    /// <summary>The analyzer itself failed, which says nothing about the spec it was reading.</summary>
    internal static readonly DiagnosticDescriptor AnalyzerFault = new(
        "SR1093",
        "The SlideRule spec analyzer failed",
        "The SlideRule spec analyzer failed at this call and may have missed literals in this project: "
        + "{0}: {1}. This is a fault in the analyzer and says nothing about the spec, which is validated "
        + "in full when it is loaded. Report it at https://github.com/andypgray/sliderule/issues.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "An internal fault stopped the spec analyzer at one call. It is reported once per compilation, as "
        + "a warning of its own, so that a build treating warnings as errors can exclude it by ID. "
        + "Spec-load validation is unaffected.",
        HelpLink);

    /// <summary>
    ///     The compiler handed the analyzer another version of the contract than its own, so it read no
    ///     literal: the fault's ID, with a message of its own.
    /// </summary>
    internal static readonly DiagnosticDescriptor VersionMismatch = new(
        "SR1093",
        "The SlideRule spec analyzer failed",
        "The SlideRule spec analyzer checked no literal in this project: it is version {0}, and the compiler "
        + "loaded version {1} of SlideRule for it. That happens when a project references "
        + "SlideRule and SlideRule.Analyzers at two different versions, so keep the two "
        + "packages on one version. The spec is still validated in full when it is loaded.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "The spec analyzer was built as one version and the compiler loaded another version of "
        + "SlideRule for it, so it checked no literal in this project. It is reported under the "
        + "fault's ID so that a build treating warnings as errors can exclude both by that one ID. Spec-load "
        + "validation is unaffected.",
        HelpLink,
        WellKnownDiagnosticTags.CompilationEnd);

    // The descriptors a verdict can be reported under, which are all the lookup below holds: the fault's two
    // share one ID, and neither is a verdict.
    private static readonly ImmutableArray<DiagnosticDescriptor> Verdicts = ImmutableArray.Create(
        DuplicateId, IdExtendsScope, BlankProse, MultiLineProse, MalformedId, DuplicateLayerName,
        BlankMemberName, BlankPattern, UnanchoredSubtreePattern, BlankProjectPattern, BlankTargetFramework,
        CounterpartTemplateWithoutPlaceholder, MalformedCitation, BlankLayerName, BlankPackagePattern, BlankProperty,
        MalformedPathGlob);

    /// <summary>Every descriptor this analyzer can report, which is what it declares it supports.</summary>
    internal static ImmutableArray<DiagnosticDescriptor> All { get; } = Verdicts.Add(AnalyzerFault)
        .Add(VersionMismatch);

    // Built from the written-out verdicts rather than from the code enum: a code with no entry here has no
    // descriptor, and no descriptor means no report.
    private static readonly ImmutableDictionary<string, DiagnosticDescriptor> ById =
        Verdicts.ToImmutableDictionary(descriptor => descriptor.Id, StringComparer.Ordinal);

    /// <summary>
    ///     The descriptor a verdict of this code is reported under, or null when this analyzer carries
    ///     none for it.
    /// </summary>
    /// <remarks>
    ///     A null answer must leave the caller silent. Reporting a descriptor the analyzer did not declare
    ///     as supported throws inside the analyzer driver, which reaches the developer as the compiler's
    ///     own <c>AD0001</c> and fails any build treating warnings as errors.
    /// </remarks>
    private static DiagnosticDescriptor? For(int code)
    {
        int number = CodeOffset + code;
        string id = IdPrefix + number.ToString(CultureInfo.InvariantCulture);

        return ById.TryGetValue(id, out DiagnosticDescriptor? descriptor) ? descriptor : null;
    }

    /// <summary>
    ///     The warning one literal draws, at <paramref name="location" />, the literal's own span, or null when
    ///     <paramref name="code" /> has no descriptor. <paramref name="head" /> and <paramref name="tail" /> are
    ///     the verdict's sentence before and after its subject clause.
    /// </summary>
    internal static Diagnostic? AtTheLiteral(
        int code, string codeName, string slot, string head, string tail, Location location)
    {
        DiagnosticDescriptor? descriptor = For(code);
        if (descriptor is null) return null;

        ImmutableDictionary<string, string?> properties = PropertiesOf(codeName, slot, head, tail);

        return Diagnostic.Create(descriptor, location, properties, head + tail);
    }

    /// <summary>
    ///     The warning several literals draw between them, at <paramref name="location" />, with
    ///     <paramref name="related" /> the other literals that took part; null when the code has no descriptor.
    /// </summary>
    /// <remarks>
    ///     A collision names no subject, so the whole sentence rides as the head and the tail is empty.
    ///     A reader of the properties can therefore treat head and tail alike whichever kind of verdict it
    ///     came from.
    /// </remarks>
    internal static Diagnostic? Collision(
        int code, string codeName, string slot, string sentence, Location location, IEnumerable<Location> related)
    {
        DiagnosticDescriptor? descriptor = For(code);
        if (descriptor is null) return null;

        ImmutableDictionary<string, string?> properties = PropertiesOf(codeName, slot, sentence, string.Empty);

        return Diagnostic.Create(descriptor, location, related, properties, sentence);
    }

    /// <summary>
    ///     The warning an analyzer fault becomes, at <paramref name="location" />, where the analysis was when it
    ///     failed: the exception's type name and the first line of its message, so a reader can tell two faults
    ///     apart without a stack trace in a build log.
    /// </summary>
    internal static Diagnostic AnalyzerFailed(Exception exception, Location location)
    {
        return Diagnostic.Create(AnalyzerFault, location, exception.GetType().Name, Opening(exception.Message));
    }

    /// <summary>
    ///     The warning for an analyzer handed another version of the contract than its own, naming both
    ///     versions, the analyzer's first.
    /// </summary>
    internal static Diagnostic VersionsMismatched(string analyzerVersion, string contractVersion)
    {
        return Diagnostic.Create(VersionMismatch, Location.None, analyzerVersion, contractVersion);
    }

    // The first line alone, because a build surface shows a warning on one row; the trailing full stop
    // comes off because the message format puts one after it.
    private static string Opening(string message)
    {
        int lineBreak = message.IndexOf('\n');
        string first = lineBreak < 0 ? message : message.Substring(0, lineBreak);
        string trimmed = first.Trim();
        bool ended = trimmed.Length > 0 && trimmed[trimmed.Length - 1] == '.';

        return ended ? trimmed.Substring(0, trimmed.Length - 1) : trimmed;
    }

    // The four keys a reader takes a verdict apart by without re-parsing the printed message. They are
    // what the parity between this analyzer and spec-load validation is measured on, so the set is closed:
    // nothing here is optional and no key carries a null. The code is the enum member's name, as the
    // contract's own code renders it.
    private static ImmutableDictionary<string, string?> PropertiesOf(
        string codeName, string slot, string head, string tail)
    {
        ImmutableDictionary<string, string?>.Builder properties =
            ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        properties.Add("code", codeName);
        properties.Add("slot", slot);
        properties.Add("head", head);
        properties.Add("tail", tail);

        return properties.ToImmutable();
    }
}
