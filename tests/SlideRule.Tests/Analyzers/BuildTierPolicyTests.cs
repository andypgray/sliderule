using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Shouldly;
using SlideRule.Analyzers;
using Xunit;

namespace SlideRule.Tests.Analyzers;

/// <summary>
///     Which rules a host that keeps a descriptor per rule declines before checking, for a descriptor that ships
///     off. No shipped host has one, so the descriptor here is made for the test; the settings that switch a
///     shipped-on descriptor off in each form are held over the guidance analyzer.
/// </summary>
public sealed class BuildTierPolicyTests
{
    private const string Rule = "test/off-by-default";

    // Release tracking is for an analyzer that ships the descriptor; this one never leaves the test.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor ShipsOff = new(
        "SR9999", "Off by default", "{0}", "SlideRule.Test", DiagnosticSeverity.Warning, isEnabledByDefault: false);
#pragma warning restore RS2008

    private static readonly BuildTierPolicy Policy = BuildTierPolicy.Supplied(
        SourceText.From(""), new Dictionary<string, DiagnosticDescriptor> { [Rule] = ShipsOff },
        BuildTierDescriptors.AnalyzerFault);

    [Fact]
    public void ARuleThatShipsOff_AndNoSettingNames_IsDeclined()
    {
        (Compilation compilation, AnalyzerConfigOptionsProvider options) = BuildTier.Configured(BuildTier.OwnCompilation());

        Policy.SwitchedOff(compilation, new AnalyzerOptions([], options), Ct)
            .ShouldBe([Rule]);
    }

    [Fact]
    public void ARuleThatShipsOff_SwitchedOnByItsId_IsKept()
    {
        Compilation switchedOn = BuildTier.WithSeverity(BuildTier.OwnCompilation(), ReportDiagnostic.Warn, ShipsOff.Id);
        (Compilation compilation, AnalyzerConfigOptionsProvider options) = BuildTier.Configured(switchedOn);

        Policy.SwitchedOff(compilation, new AnalyzerOptions([], options), Ct)
            .ShouldBeEmpty();
    }
}
