using System.Reflection;
using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Analyzers;
using Xunit;

namespace SlideRule.Tests.Analyzers;

/// <summary>
///     The six warnings the build tier declares: their IDs, the title, message and description each one
///     ships with, and the tag that says which of them a host must run a compilation end action to get.
/// </summary>
/// <remarks>
///     <para>
///         Every string here is what a developer reads in a build log and what an estate's warning
///         suppressions are written against, so each is pinned whole rather than by a fragment. The
///         <c>CompilationEnd</c> tag is pinned as an inventory in both directions: the descriptors that are
///         reported from an end action carry it, and the three that are not — the two rule warnings, reported
///         per document, and the unreadable file, reported per additional file — do not.
///     </para>
///     <para>
///         The fault's ID carries two descriptors, the fault and the version difference, so the wording is
///         pinned per descriptor field rather than per ID.
///     </para>
/// </remarks>
public sealed class BuildTierDescriptorTests
{
    private const string HelpLink =
        "https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md#diagnostics";

    /// <summary>Each descriptor by its field, with the ID, title, message format and description it ships with.</summary>
    public static TheoryData<string, string, string, string, string> WordingCases =>
        new()
        {
            {
                nameof(BuildTierDiagnostics.BrokenRule),
                "SR2001",
                "An architecture rule is broken",
                "{0}",
                "Code in this project breaks a rule of the architecture spec. The message carries the rule's "
                + "ID, its sentence, the reason it exists, how to fix the violation and, where the rule names "
                + "one, the type to copy. 'sliderule check' gives the whole-solution verdict and stays the "
                + "gate; this warning is the same finding, reported while the project builds."
            },
            {
                nameof(BuildTierDiagnostics.NewDebt),
                "SR2002",
                "A Migrate rule has a violation its baseline does not hold",
                "{0}",
                "Code in this project adds a violation that a Migrate rule's baseline does not hold, or adds "
                + "sites to a pair it does. Violations the baseline already holds are not reported."
            },
            {
                nameof(BuildTierDiagnostics.NoModelFile),
                "SR2090",
                "No SlideRule model file was found",
                "No sliderule.model.json was found above this project, so this build checked no "
                + "architecture rule. Run 'sliderule render --model' in the solution directory and commit "
                + "the file it writes.",
                "The analyzer reads the architecture from sliderule.model.json, which 'sliderule render "
                + "--model' writes beside the solution. Without it the build checks nothing."
            },
            {
                nameof(BuildTierDiagnostics.UnreadableFile),
                "SR2091",
                "A SlideRule file could not be read",
                "{0}",
                "A model file or a baseline file the build handed to the analyzer is malformed, fails its "
                + "integrity check, or was written in a version this analyzer does not read. The warning "
                + "sits in that file at the place the read stopped."
            },
            {
                nameof(BuildTierDiagnostics.UncheckedRule),
                "SR2092",
                "A ratcheted rule was not checked because the build was given no baseline for it",
                "Rule '{0}' was not checked in this build. It is ratcheted and the build was given no "
                + "captured baseline for it, so checking it here would report its existing violations as "
                + "new. If its baseline file is kept outside arch/baselines/, add that file to the project "
                + "as a SlideRuleBaseline item; if the rule has no captured baseline yet, 'sliderule "
                + "baseline init' captures one.",
                "A ratcheted rule is checked in the build only against its captured baseline. Without one, "
                + "the build would report everything the baseline exists to grandfather, so the rule is "
                + "left to 'sliderule check'. The warning appears only where the rule would have reported "
                + "in this project."
            },
            {
                nameof(BuildTierDiagnostics.AnalyzerFault),
                "SR2093",
                "The SlideRule analyzer failed",
                "The SlideRule analyzer failed in this project, so some or all of its findings are "
                + "missing: {0}: {1}. This is a fault in the analyzer and says nothing about the code, and "
                + "'sliderule check' is unaffected. Report it at "
                + "https://github.com/andypgray/sliderule/issues.",
                "An internal fault stopped the analyzer in this project. It is reported as a warning of its "
                + "own so that a build treating warnings as errors can exclude it by ID."
            },
            {
                nameof(BuildTierDiagnostics.VersionMismatch),
                "SR2093",
                "The SlideRule analyzer failed",
                "The SlideRule analyzer checked nothing in this project: it is version {0}, and the compiler "
                + "loaded version {1} of SlideRule for it. That happens when a project references "
                + "SlideRule and SlideRule.Analyzers at two different versions, so keep the two "
                + "packages on one version. 'sliderule check' is unaffected.",
                "The analyzer was built as one version and the compiler loaded another version of "
                + "SlideRule for it, so it checked nothing in this project. It is reported under the "
                + "fault's ID so that a build treating warnings as errors can exclude both by that one ID."
            }
        };

    [Theory]
    [MemberData(nameof(WordingCases))]
    public void Descriptor_OfEachField_CarriesTheIdTitleMessageAndDescriptionItShipsWith(
        string field, string id, string title, string messageFormat, string description)
    {
        DiagnosticDescriptor descriptor = Declared(field);

        descriptor.Id.ShouldBe(id);
        descriptor.Title.Invariant()
            .ShouldBe(title);
        descriptor.MessageFormat.Invariant()
            .ShouldBe(messageFormat);
        descriptor.Description.Invariant()
            .ShouldBe(description);
    }

    [Fact]
    public void EveryDescriptorField_HasItsWordingPinnedAbove()
    {
        List<string> declared = typeof(BuildTierDiagnostics)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => field.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        List<string> pinned = WordingCases
            .Select(row => row.Data.Item1)
            .Order(StringComparer.Ordinal)
            .ToList();

        pinned.ShouldBe(declared);
    }

    [Fact]
    public void EveryDescriptor_IsAnEnabledWarningInTheSlideRuleCategoryWithTheSameHelpLink()
    {
        List<string> declared = BuildTierDiagnostics.All
            .Select(descriptor =>
                $"{descriptor.Id} {descriptor.Category} {descriptor.DefaultSeverity} "
                + $"{descriptor.IsEnabledByDefault} {descriptor.HelpLinkUri}")
            .ToList();

        declared.ShouldBe(
        [
            $"SR2001 SlideRule Warning True {HelpLink}",
            $"SR2002 SlideRule Warning True {HelpLink}",
            $"SR2090 SlideRule Warning True {HelpLink}",
            $"SR2091 SlideRule Warning True {HelpLink}",
            $"SR2092 SlideRule Warning True {HelpLink}",
            $"SR2093 SlideRule Warning True {HelpLink}",
            $"SR2093 SlideRule Warning True {HelpLink}"
        ]);
    }

    [Fact]
    public void TheCompilationEndTag_IsOnEveryDescriptorReportedFromAnEndActionAndOnNoOther()
    {
        List<string> tagged = BuildTierDiagnostics.All
            .Where(descriptor => descriptor.CustomTags.Contains(WellKnownDiagnosticTags.CompilationEnd))
            .Select(descriptor => descriptor.Id)
            .ToList();

        tagged.ShouldBe(["SR2090", "SR2092", "SR2093", "SR2093"]);
    }

    [Fact]
    public void TheAnalyzer_DeclaresTheSixIdsInSevenDescriptorsAsWhatItCanReport()
    {
        List<string> supported = new BuildTierAnalyzer().SupportedDiagnostics
            .Select(descriptor => descriptor.Id)
            .ToList();

        supported.ShouldBe(["SR2001", "SR2002", "SR2090", "SR2091", "SR2092", "SR2093", "SR2093"]);
    }

    // A descriptor by the field that declares it, which is the one name two descriptors under one ID do not share.
    private static DiagnosticDescriptor Declared(string field)
    {
        FieldInfo declared = typeof(BuildTierDiagnostics)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Static)
            .ShouldNotBeNull($"BuildTierDiagnostics should declare {field}");

        return (DiagnosticDescriptor)declared.GetValue(null)!;
    }
}
