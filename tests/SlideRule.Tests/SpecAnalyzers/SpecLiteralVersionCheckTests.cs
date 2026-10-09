using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using SlideRule.SpecAnalyzers;

namespace SlideRule.Tests.SpecAnalyzers;

/// <summary>
///     The spec analyzer's version check: a copy of the contract from another version than the analyzer's own
///     is one <c>SR1093</c> saying so, and the analyzer then reads no literal in the compilation.
/// </summary>
/// <remarks>
///     The check reads the two informational versions up to the <c>+</c>; the seam hands it two strings in
///     their place, so these facts vary the versions and never the assemblies. The spec every fact compiles
///     draws one verdict when the versions agree, so a version difference that silenced it is visible as the
///     verdict going missing.
/// </remarks>
public sealed class SpecLiteralVersionCheckTests
{
    private static readonly string MalformedId = SpecSnippet.Around("""arch.Rule("Bad_Id");""");

    // A project that never references the contract, which the analyzer has nothing to read in.
    private const string NoSpec =
        """
        public sealed class Plain
        {
        }
        """;

    [Fact]
    public async Task TwoVersions_AreOneLB1093NamingBothAndNoLiteralWarning()
    {
        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync(MalformedId, ("0.9.0+abc123", "0.8.0+def456"));

        Diagnostic mismatch = reported.ShouldBeTheOnly("SR1093");
        mismatch.ShouldSay(Mismatch("0.9.0", "0.8.0"));
        mismatch.Location.ShouldBe(Location.None);
    }

    [Fact]
    public async Task OneVersion_ReportsTheVerdictAsUsual()
    {
        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync(MalformedId, ("0.9.0", "0.9.0"));

        reported.ShouldBeTheOnly("SR1008");
    }

    [Fact]
    public async Task TheCommitAfterThePlus_IsNotPartOfTheVersion()
    {
        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync(MalformedId, ("0.9.0+aaa", "0.9.0+bbb"));

        reported.ShouldBeTheOnly("SR1008");
    }

    [Theory]
    [InlineData(null, "0.9.0", "", "0.9.0")]
    [InlineData("0.9.0", null, "0.9.0", "")]
    public async Task AMissingVersion_NeverMatchesARealOne(
        string? analyzer, string? contract, string shownAnalyzer, string shownContract)
    {
        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync(MalformedId, (analyzer, contract));

        reported.ShouldBeTheOnly("SR1093")
            .ShouldSay(Mismatch(shownAnalyzer, shownContract));
    }

    [Fact]
    public async Task AProjectWithNoMarker_IsNeverChecked()
    {
        var reached = false;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(
            [(SpecSnippet.FilePath, NoSpec)],
            SpecSnippet.WithoutTheContract,
            cancellationToken: Ct,
            versions: () =>
            {
                reached = true;
                return ("0.9.0", "0.8.0");
            });

        reported.ShouldBeSilent();
        reached.ShouldBeFalse("a project with no marker should not have run the version check");
    }

    [Fact]
    public async Task AFaultInTheCheck_IsTheOrdinaryFaultOnceAndPlacedNowhere()
    {
        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(
            [(SpecSnippet.FilePath, MalformedId)],
            cancellationToken: Ct,
            versions: () => throw new InvalidOperationException("The contract went missing.\nA second line."));

        Diagnostic fault = reported.ShouldBeTheOnly("SR1093");
        fault.ShouldSay(
            "The SlideRule spec analyzer failed at this call and may have missed literals in this project: "
            + "InvalidOperationException: The contract went missing. This is a fault in the analyzer and says "
            + "nothing about the spec, which is validated in full when it is loaded. Report it at "
            + "https://github.com/andypgray/sliderule/issues.");
        fault.Location.ShouldBe(Location.None);
    }

    [Fact]
    public void TheVersionsReadOffTheBuiltAssemblies_AreOneReleasedVersion()
    {
        // The check's own read, over the assemblies this suite was built with: the same version on both
        // sides, and a real one, because two missing versions would agree as well.
        string analyzer = SpecLiteralAnalyzer.ReleasedVersion(
            SpecLiteralAnalyzer.InformationalVersionOf(typeof(SpecLiteralAnalyzer).Assembly));
        string contract = SpecLiteralAnalyzer.ReleasedVersion(
            SpecLiteralAnalyzer.InformationalVersionOf(typeof(IArchitectureSpec).Assembly));

        analyzer.ShouldNotBeEmpty();
        analyzer.ShouldNotContain("+");
        contract.ShouldBe(analyzer);
    }

    [Theory]
    [InlineData("0.9.0+0123abc", "0.9.0")]
    [InlineData("0.9.0", "0.9.0")]
    [InlineData("1.0.0-beta.2+abc", "1.0.0-beta.2")]
    [InlineData(null, "")]
    public void AReleasedVersion_IsTheInformationalVersionUpToItsFirstPlus(string? informational, string released)
    {
        SpecLiteralAnalyzer.ReleasedVersion(informational)
            .ShouldBe(released);
    }

    private static string Mismatch(string analyzer, string contract)
    {
        return $"The SlideRule spec analyzer checked no literal in this project: it is version {analyzer}, "
               + $"and the compiler loaded version {contract} of SlideRule for it. That happens when a "
               + "project references SlideRule and SlideRule.Analyzers at two different "
               + "versions, so keep the two packages on one version. The spec is still validated in full when "
               + "it is loaded.";
    }

    private static Task<IReadOnlyList<Diagnostic>> DiagnoseAsync(
        string source, (string? Analyzer, string? Core) versions)
    {
        return SpecSnippet.ShouldCompileAndDiagnoseAsync(
            [(SpecSnippet.FilePath, source)], cancellationToken: Ct, versions: () => versions);
    }
}
