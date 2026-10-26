using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.SpecAnalyzers;
using SlideRule.Tests.DocHygiene;
using SlideRule.Validation;
using Xunit;

namespace SlideRule.Tests.SpecAnalyzers;

/// <summary>
///     The warnings the spec analyzer declares: their IDs, the title, message and description each one
///     ships with, the numbering that ties every one but the tool fault to a validation code, and the claim
///     that those are exactly the verdicts the contract can hand back.
/// </summary>
/// <remarks>
///     <para>
///         Every string here is what a developer reads in a build log and what an estate's warning
///         suppressions are written against, so each is pinned whole rather than by a fragment. The fault's
///         ID carries two descriptors, the fault and the version difference, so the wording is pinned per
///         descriptor field rather than per ID.
///     </para>
///     <para>
///         The last arm is the one that makes writing the descriptors out by hand safe rather than
///         brittle. A code an owner can return with no descriptor here would be dropped in silence, and a
///         descriptor no owner returns is dead wording nobody would ever see; driving the owners over a
///         handful of literals and comparing the codes that come back catches both.
///     </para>
/// </remarks>
public sealed class SpecLiteralDescriptorTests
{
    private const string HelpLink =
        "https://github.com/andypgray/sliderule/blob/main/src/SlideRule/README.md#diagnostics";

    /// <summary>The ID of the one warning that is about the analyzer rather than about a spec.</summary>
    private const string ToolFault = "SR1093";

    /// <summary>
    ///     Every descriptor's ID, in the order the analyzer declares them: the fault's twice, once for the fault
    ///     and once for the version difference.
    /// </summary>
    private static readonly string[] Ids =
    [
        "SR1000", "SR1001", "SR1005", "SR1006", "SR1008", "SR1010", "SR1012", "SR1016", "SR1017", "SR1024",
        "SR1025", "SR1026", "SR1030", "SR1031", "SR1032", "SR1033", "SR1035", ToolFault, ToolFault
    ];

    /// <summary>The closing sentence every verdict description carries, restated here to pin it.</summary>
    private const string Closing =
        " Loading the spec reports the same mistake as a validation error, from 'sliderule check', "
        + "'sliderule render' and the xUnit adapter alike; this warning is that verdict, reported where "
        + "the literal is written.";

    private const string Header =
        "The spec analyzer's warnings and the verdicts the contract returns have come apart. Every "
        + "descriptor is written out rather than generated from the code enum, so that a contract carrying "
        + "a code this analyzer has no wording for is passed over in silence instead of reported under "
        + "invented English — which only works while the two lists agree";

    /// <summary>
    ///     The literals the owners are driven over: blank, whitespace, a line break, a glob whose subtree
    ///     operator strands a wildcard, an ID outside the grammar, a template with no placeholder, and text
    ///     that is not a URL. Between them they reach every arm every slot carries.
    /// </summary>
    private static readonly string[] Probes =
        ["", "   ", "one\ntwo", "MyApp.*.Controllers.*", "Bad ID", "IService", "ADR 7", "/src"];

    /// <summary>Each descriptor by its field, with the ID, title, message format and description it ships with.</summary>
    public static TheoryData<string, string, string, string, string> WordingCases =>
        new()
        {
            {
                nameof(SpecLiteralDiagnostics.DuplicateId),
                "SR1000",
                "A rule or scope ID is declared twice",
                "{0}",
                "Two declarations in this method produce the same rule ID. IDs are unique across the whole "
                + "spec, counting the two rules every scope contributes of its own, "
                + "'{scope-id}/containment' and '{scope-id}/tripwire', so two scopes declared with one ID "
                + "are reported under those two IDs, which appear nowhere in the source. The warning sits "
                + "on the later declaration and gives the first as a related location. Only declarations "
                + "inside one method body are compared here; a duplicate across methods, files or a rule "
                + "pack is reported when the spec is loaded. Rename one of them." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.IdExtendsScope),
                "SR1001",
                "A rule ID sits under a scope's reserved prefix",
                "{0}",
                "A rule's ID equals a scope's ID, or begins with it and a slash. A scope reserves "
                + "'{scope-id}/' for the rules it contributes, 'containment' and 'tripwire'. Give the rule "
                + "an ID outside that prefix. Only a rule and a scope declared inside one method body are "
                + "compared here." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.BlankProse),
                "SR1005",
                "A prose argument is blank",
                "{0}",
                "A prose argument is empty or only whitespace: a Because, a Fix, a Citation, a migration's "
                + "'from' description, a Baseline path, a scope's Dragons or DragonsDoc, a layer's "
                + "Purpose, or the description a Where or a custom Must requires. Write the line, or drop "
                + "the call." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.MultiLineProse),
                "SR1006",
                "A prose argument spans more than one line",
                "{0}",
                "A prose argument carries a line break. Every prose value is a single line; link a longer "
                + "document with a scope's DragonsDoc instead." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.MalformedId),
                "SR1008",
                "A rule or scope ID is malformed",
                "{0}",
                "A rule or scope ID must be lowercase letters, digits and hyphens, in segments joined by "
                + "'/': it has to match ^[a-z0-9-]+(/[a-z0-9-]+)*$. The convention is 'area/rule-name'."
                + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.DuplicateLayerName),
                "SR1010",
                "A layer name is declared twice",
                "{0}",
                "Two arch.Layer calls in this method give the same name. Layer names are unique within a "
                + "spec, because the name is how every rule, report and generated context refers to the "
                + "layer. The warning sits on the later declaration and gives the first as a related "
                + "location. Only layers declared inside one method body are compared here." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.BlankMemberName),
                "SR1012",
                "A member name is blank",
                "{0}",
                "An arch.Member(typeof(T), name) was given an empty member name. Pass the name the type "
                + "declares, ideally as nameof(...)." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.BlankPattern),
                "SR1016",
                "A pattern, name or affix is blank",
                "{0}",
                "A glob, name or affix is empty or only whitespace: a namespace glob, a type-name glob, an "
                + "exact name on Named, a project name on arch.Project or MustResideInProject, a suffix or "
                + "prefix, a MustHaveExactlyOneCounterpart name template, or a string-named attribute, "
                + "interface, base type, return type or parameter type. A blank affix is true of every "
                + "name and a blank glob fails when the spec is checked, so neither is likely to be what "
                + "was meant." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.UnanchoredSubtreePattern),
                "SR1017",
                "A namespace pattern can never match",
                "{0}",
                "A namespace glob ends in '.*' and the text before that ending also contains a '*', such "
                + "as 'MyApp.*.Controllers.*'. Everything before a trailing '.*' is matched literally, so "
                + "such a glob matches nothing. Anchor the subtree on a literal prefix "
                + "('MyApp.Web.Controllers.*'), or drop the trailing '.*' and let the interior '*' match "
                + "one segment." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.BlankProjectPattern),
                "SR1024",
                "A project name or pattern is blank",
                "{0}",
                "An arch.Projects.Named was given an empty project name, or an arch.Projects.Matching an "
                + "empty glob. A blank name matches no project and a blank glob matches every one, so "
                + "neither is likely to be what was meant." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.BlankTargetFramework),
                "SR1025",
                "A target framework is blank",
                "{0}",
                "A MustOnlyTarget or MustTarget was given an empty target framework. A blank moniker matches "
                + "nothing. On MustOnlyTarget it narrows the permitted list instead of widening it, so the "
                + "rule stays green until some project targets the framework that was meant to be allowed; "
                + "on MustTarget it requires a framework no project targets, so the rule fails every project "
                + "it reads." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.CounterpartTemplateWithoutPlaceholder),
                "SR1026",
                "A counterpart name template has no {Name} placeholder",
                "{0}",
                "A MustHaveExactlyOneCounterpart name template carries no '{Name}' placeholder, such as "
                + "'IService'. Every subject would derive the same fixed name, which makes the rule a "
                + "claim about how many types carry that one name and says nothing about correspondence. "
                + "Write a template such as 'I{Name}'. Substitution is case-sensitive, so '{name}' is the "
                + "usual cause." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.MalformedCitation),
                "SR1030",
                "A citation is not an absolute http(s) URL",
                "{0}",
                "A Citation is not an absolute http or https URL: a pasted page title, a "
                + "repository-relative path, or a URI on another scheme. Every surface renders the value "
                + "as a link and hands it on unread (the generated agent context autolinks it, and SARIF "
                + "output publishes it as the rule's help URI), so nothing downstream could report a bad "
                + "one." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.BlankLayerName),
                "SR1031",
                "A layer name is blank",
                "{0}",
                "An arch.Layer was given a name that is empty or only whitespace. The name is how every "
                + "rule, report and generated context refers to the layer, and how a second layer is told "
                + "from it. Name the layer in the first argument." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.BlankPackagePattern),
                "SR1032",
                "A package pattern is blank",
                "{0}",
                "A MustOnlyReferencePackages or MustNotReferencePackages was given an empty package pattern. "
                + "No package has an empty identifier, so a blank pattern matches nothing. On "
                + "MustOnlyReferencePackages it permits nothing, so the rule fails on the package that was "
                + "meant to be allowed; on MustNotReferencePackages it forbids nothing, so the rule stays "
                + "green whatever the projects reference." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.BlankProperty),
                "SR1033",
                "A property name or value is blank",
                "{0}",
                "A MustSetProperty was given an empty property name or value. No property has an empty name, and "
                + "MSBuild treats a property set to nothing as unset, so either way the rule asks for something no "
                + "project can declare and fails every project it reads." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.MalformedPathGlob),
                "SR1035",
                "A path glob is malformed",
                "{0}",
                "An Under was given a path glob that is rooted, contains a backslash, or has a `.`, `..` or empty "
                + "segment or a `**` inside a longer segment. A path glob is relative to the solution directory "
                + "with `/` between segments, so a glob in any of those shapes matches no declaring file." + Closing
            },
            {
                nameof(SpecLiteralDiagnostics.AnalyzerFault),
                ToolFault,
                "The SlideRule spec analyzer failed",
                "The SlideRule spec analyzer failed at this call and may have missed literals in this "
                + "project: {0}: {1}. This is a fault in the analyzer and says nothing about the spec, "
                + "which is validated in full when it is loaded. Report it at "
                + "https://github.com/andypgray/sliderule/issues.",
                "An internal fault stopped the spec analyzer at one call. It is reported once per "
                + "compilation, as a warning of its own, so that a build treating warnings as errors can "
                + "exclude it by ID. Spec-load validation is unaffected."
            },
            {
                nameof(SpecLiteralDiagnostics.VersionMismatch),
                ToolFault,
                "The SlideRule spec analyzer failed",
                "The SlideRule spec analyzer checked no literal in this project: it is version {0}, and the "
                + "compiler loaded version {1} of SlideRule for it. That happens when a project "
                + "references SlideRule and SlideRule.Analyzers at two different versions, so "
                + "keep the two packages on one version. The spec is still validated in full when it is loaded.",
                "The spec analyzer was built as one version and the compiler loaded another version of "
                + "SlideRule for it, so it checked no literal in this project. It is reported under the "
                + "fault's ID so that a build treating warnings as errors can exclude both by that one ID. "
                + "Spec-load validation is unaffected."
            }
        };

    [Theory]
    [MemberData(nameof(WordingCases))]
    public void Descriptor_OfEachField_CarriesTheIdTitleMessageAndDescriptionItShipsWith(
        string field, string id, string title, string messageFormat, string description)
    {
        DiagnosticDescriptor descriptor = Declared()
            .Where(entry => entry.Field == field)
            .Select(entry => entry.Descriptor)
            .ShouldHaveSingleItem($"SpecLiteralDiagnostics should declare {field}");

        descriptor.Id.ShouldBe(id);
        descriptor.Title.Invariant()
            .ShouldBe(title);
        descriptor.MessageFormat.Invariant()
            .ShouldBe(messageFormat);
        descriptor.Description.Invariant()
            .ShouldBe(description);
    }

    [Fact]
    public void EveryDescriptor_IsAnEnabledWarningInTheSlideRuleCategoryWithTheSameHelpLink()
    {
        List<string> declared = SpecLiteralDiagnostics.All
            .Select(descriptor =>
                $"{descriptor.Id} {descriptor.Category} {descriptor.DefaultSeverity} "
                + $"{descriptor.IsEnabledByDefault} {descriptor.HelpLinkUri}")
            .ToList();
        List<string> expected = Ids
            .Select(id => $"{id} SlideRule Warning True {HelpLink}")
            .ToList();

        declared.ShouldBe(expected);
    }

    [Fact]
    public void EveryDescriptorField_HasItsWordingPinnedAbove()
    {
        List<string> declared = Declared()
            .Select(entry => entry.Field)
            .Order(StringComparer.Ordinal)
            .ToList();
        List<string> pinned = WordingCases
            .Select(row => row.Data.Item1)
            .Order(StringComparer.Ordinal)
            .ToList();

        pinned.ShouldBe(declared);
    }

    [Fact]
    public void TheOnlyCustomTag_IsCompilationEndOnTheVersionMismatch()
    {
        // CompilationEnd is the one that would bite on a verdict: it tells a host the warning needs a
        // whole-compilation pass, which is exactly the live squiggle these exist to have. The version
        // difference is reported from an end action, is about the project rather than a literal, and carries
        // it for that reason.
        List<string> tagged = Declared()
            .Where(entry => entry.Descriptor.CustomTags.Any())
            .Select(entry => $"{entry.Field} carries {string.Join(", ", entry.Descriptor.CustomTags)}.")
            .ToList();

        tagged.ShouldBe([$"{nameof(SpecLiteralDiagnostics.VersionMismatch)} carries {WellKnownDiagnosticTags.CompilationEnd}."]);
    }

    [Fact]
    public void TheAnalyzer_DeclaresEveryVerdictIdOnceAndTheFaultIdTwiceAsWhatItCanReport()
    {
        List<string> supported = new SpecLiteralAnalyzer().SupportedDiagnostics
            .Select(descriptor => descriptor.Id)
            .ToList();

        supported.ShouldBe(Ids);
    }

    [Fact]
    public void EveryVerdictDescriptor_TakesItsIdFromTheCodeItsFieldIsNamedFor()
    {
        List<string> findings = new();

        foreach ((string field, DiagnosticDescriptor descriptor) in Declared())
        {
            if (descriptor.Id == ToolFault) continue;

            if (!Enum.TryParse(field, out SpecValidationErrorCode code))
            {
                findings.Add($"{field} names no SpecValidationErrorCode member, so nothing ties "
                             + $"{descriptor.Id} to a verdict the contract can return.");
                continue;
            }

            string expected = "SR" + (1000 + (int)code).ToString(CultureInfo.InvariantCulture);
            if (descriptor.Id != expected)
                findings.Add($"{field} is {descriptor.Id}, and {code} is {(int)code}, which is {expected}.");
        }

        findings.ShouldReportNothing(Header);
    }

    [Fact]
    public void EveryValidationCode_SitsBelowTheNumbersTheAnalyzerKeepsForItsOwnFaults()
    {
        List<string> findings = Enum.GetValues<SpecValidationErrorCode>()
            .Where(code => (int)code >= 90)
            .Select(code => $"{code} is {(int)code}, so it would be reported as "
                            + $"SR{1000 + (int)code}, inside the SR1090 to SR1099 range the analyzer keeps "
                            + "for faults of its own.")
            .ToList();

        findings.ShouldReportNothing(Header);
    }

    [Fact]
    public void TheVerdictDescriptors_AreExactlyTheCodesTheContractCanReturn()
    {
        // Arrange: every slot over every probe, and both collision owners over input that collides three
        // ways at once.
        var returned = new SortedSet<string>(StringComparer.Ordinal);
        IdDeclaration[] colliding =
        [
            new("shape/suffix", IdRole.Rule, 0),
            new("shape/suffix", IdRole.Rule, 1),
            new("legacy/billing", IdRole.Scope, 2),
            new("legacy/billing/extra", IdRole.Rule, 3)
        ];

        // Act
        foreach (SpecSlot slot in SpecSlot.All)
        foreach (string probe in Probes)
            if (slot.Decide(probe) is { } verdict)
                returned.Add(verdict.Code.ToString());

        foreach (CollisionVerdict verdict in SpecIdCollisions.Decide(colliding))
            returned.Add(verdict.Code.ToString());

        foreach (CollisionVerdict verdict in SpecLayerNames.Decide(["Web", "Web"]))
            returned.Add(verdict.Code.ToString());

        // Assert
        List<string> described = Declared()
            .Where(entry => entry.Descriptor.Id != ToolFault)
            .Select(entry => entry.Field)
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToList();
        returned.ToList()
            .ShouldBe(described);
    }

    // The descriptor fields by name, which is what ties each one to the validation code it reports: the
    // field is named for the code, and the ID is derived from the code's number.
    private static List<(string Field, DiagnosticDescriptor Descriptor)> Declared()
    {
        return typeof(SpecLiteralDiagnostics)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (field.Name, (DiagnosticDescriptor)field.GetValue(null)!))
            .ToList();
    }
}
