using Shouldly;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     What <see cref="SpecfulCodebase" /> promises the gates that read it: the models it hands back are the
///     verbs' own extraction, the target-framework map included.
/// </summary>
public sealed class SpecfulCodebaseTests
{
    private const string Core = "MultiTfm.Core";

    // Pattern-only, so the spec assembly needs no reference to the fixture at all. The rule only has to
    // exist: what is under test is the extraction the spec rides beside.
    private const string CoreIndependentSpec = """
                                               using SlideRule;

                                               namespace MultiTfmSpecs
                                               {
                                                   public sealed class SpecfulMultiTfmSpec : IArchitectureSpec
                                                   {
                                                       public void Define(Arch arch)
                                                       {
                                                           arch.Rule("layering/core-independent")
                                                               .Enforce(arch.Project("MultiTfm.Core").MustNotReference(arch.Namespace("MultiTfm.Web.*")))
                                                               .Because("The core must not know about the web tier.");
                                                       }
                                                   }
                                               }
                                               """;

    private static readonly string SolutionPath = FixtureBeds.MultiTfmSolution;

    /// <summary>
    ///     The framework map reaches the extraction. No project in this repository compiles for more than one
    ///     framework, so the self models could lose the map with every gate that reads them still green; this
    ///     fixture's core project compiles twice, and only an extraction handed the map can say which
    ///     framework the types both compilations share took their facts from.
    /// </summary>
    [Fact]
    public async Task ExtractAsync_AMultiTargetedProject_CarriesTheFrameworkItsFactsFollow()
    {
        // Arrange
        string specDll = SpecAssemblyCompiler.EmitSpecDllToFreshTemp(
            "specful-codebase", "SpecfulMultiTfm", CoreIndependentSpec);

        // Act
        SpecfulCodebase extracted = await SpecfulCodebase.ExtractAsync(
            SolutionPath, specDll, FixtureBeds.MultiTfmRoot, Ct);

        // Assert: the winner rather than the project's framework list, because MSBuild evaluation fills the
        // list whether or not the extraction was handed the map, and only the winner needs it.
        ProjectNode core = extracted.Survey.Project(Core);
        core.FactsFollow.ShouldBe("net10.0");

        // The same fact as prose, whose whole sentence MultiTargetFrameworkTests pins. Here it only has to
        // be there: a merge handed no map has no framework to name, and says nothing.
        extracted.Survey.MergeNotes
            .ShouldHaveSingleItem()
            .ShouldStartWith("Project 'MultiTfm.Core' targets 'net10.0' and 'netstandard2.0';");
    }
}
