using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Analyzers;
using Xunit;

namespace SlideRule.Tests.Analyzers;

/// <summary>
///     The build analyzer's version check: a copy of SlideRule from another version than the
///     analyzer's own is one <c>SR2093</c> saying so, and the analyzer then checks nothing in the compilation.
/// </summary>
/// <remarks>
///     A project carrying the two packages at two versions hands the analyzer the other package's copy of
///     Core, and a run against it can report on the other version's code without a word, or fault where a
///     member moved. The check reads the two informational versions up to the <c>+</c>; the seam hands it two
///     strings in their place, so these facts vary the versions and never the assemblies.
/// </remarks>
public sealed class BuildTierVersionCheckTests
{
    /// <summary>Each arrangement's files, and what it reports when the two versions agree.</summary>
    public static TheoryData<string> Arrangements => ["findings", "no model file", "unchecked rule", "refused model"];

    [Theory]
    [MemberData(nameof(Arrangements))]
    public async Task TwoVersions_AreOneSR2093NamingBothAndNothingElse(string arrangement)
    {
        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync(("0.9.0+abc123", "0.8.0+def456"), arrangement);

        reported.ShouldBeTheOnly("SR2093")
            .ShouldSay(Mismatch("0.9.0", "0.8.0"));
    }

    [Fact]
    public async Task TwoVersions_UnderTheBuildTier_AreTheSameOneSR2093()
    {
        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync(("0.9.0", "0.8.0"), "findings", "build");

        reported.ShouldBeTheOnly("SR2093")
            .ShouldSay(Mismatch("0.9.0", "0.8.0"));
    }

    [Theory]
    [MemberData(nameof(Arrangements))]
    public async Task OneVersion_ReportsWhatTheArrangementReports(string arrangement)
    {
        IReadOnlyList<Diagnostic> agreed = await DiagnoseAsync(("0.9.0", "0.9.0"), arrangement);
        IReadOnlyList<Diagnostic> unarmed = await BuildTier.DiagnoseAsync(
            new BuildTierAnalyzer(), BuildTier.OwnCompilation(), FilesFor(arrangement), cancellationToken: Ct);

        agreed.ShouldNotReport("SR2093");
        string[] unarmedIds = [.. unarmed.Select(diagnostic => diagnostic.Id)];
        agreed.ShouldReport(unarmedIds);
        agreed.ShouldNotBeEmpty("the arrangement should report something for the check to have let through");
    }

    [Fact]
    public async Task TheCommitAfterThePlus_IsNotPartOfTheVersion()
    {
        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync(("0.9.0+aaa", "0.9.0+bbb"), "findings");

        reported.ShouldAllBe("SR2001", 3, "the two versions are one version, so the rule is checked as usual");
    }

    [Theory]
    [InlineData(null, "0.9.0", "", "0.9.0")]
    [InlineData("0.9.0", null, "0.9.0", "")]
    public async Task AMissingVersion_NeverMatchesARealOne(
        string? analyzer, string? core, string shownAnalyzer, string shownCore)
    {
        IReadOnlyList<Diagnostic> reported = await DiagnoseAsync((analyzer, core), "findings");

        reported.ShouldBeTheOnly("SR2093")
            .ShouldSay(Mismatch(shownAnalyzer, shownCore));
    }

    [Fact]
    public async Task TheTierOff_NeverRunsTheCheck()
    {
        var reached = false;
        var analyzer = new BuildTierAnalyzer(versions: () =>
        {
            reached = true;
            return ("0.9.0", "0.8.0");
        });

        IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(
            analyzer, BuildTier.OwnCompilation(), FilesFor("findings"), "off", Ct);

        reported.ShouldBeSilent();
        reached.ShouldBeFalse("tier off should not have run the version check");
    }

    [Fact]
    public async Task AFaultInTheCheck_IsTheOrdinaryFaultOnce()
    {
        var analyzer = new BuildTierAnalyzer(versions: () =>
        {
            BuildTier.Boom();
            return (null, null);
        });

        IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(
            analyzer, BuildTier.OwnCompilation(), FilesFor("findings"), cancellationToken: Ct);

        reported.ShouldBeTheOnly("SR2093")
            .ShouldSay(BuildTier.FaultMessage);
    }

    [Fact]
    public void TheVersionsReadOffTheBuiltAssemblies_AreOneReleasedVersion()
    {
        // The check's own read, over the assemblies this suite was built with: the same version on both
        // sides, and a real one, because two missing versions would agree as well.
        string analyzer = BuildTierAnalyzer.ReleasedVersion(
            BuildTierAnalyzer.InformationalVersionOf(typeof(BuildTierAnalyzer).Assembly));
        string core = BuildTierAnalyzer.ReleasedVersion(
            BuildTierAnalyzer.InformationalVersionOf(typeof(IArchitectureSpec).Assembly));

        analyzer.ShouldNotBeEmpty();
        analyzer.ShouldNotContain("+");
        core.ShouldBe(analyzer);
    }

    [Theory]
    [InlineData("0.9.0+0123abc", "0.9.0")]
    [InlineData("0.9.0", "0.9.0")]
    [InlineData("1.0.0-beta.2+abc", "1.0.0-beta.2")]
    [InlineData(null, "")]
    public void AReleasedVersion_IsTheInformationalVersionUpToItsFirstPlus(string? informational, string released)
    {
        BuildTierAnalyzer.ReleasedVersion(informational)
            .ShouldBe(released);
    }

    private static string Mismatch(string analyzer, string core)
    {
        return $"The SlideRule analyzer checked nothing in this project: it is version {analyzer}, and the "
               + $"compiler loaded version {core} of SlideRule for it. That happens when a project "
               + "references SlideRule and SlideRule.Analyzers at two different versions, so "
               + "keep the two packages on one version. 'sliderule check' is unaffected.";
    }

    private static Task<IReadOnlyList<Diagnostic>> DiagnoseAsync(
        (string? Analyzer, string? Core) versions, string arrangement, string? tier = null)
    {
        var analyzer = new BuildTierAnalyzer(versions: () => versions);

        return BuildTier.DiagnoseAsync(analyzer, BuildTier.OwnCompilation(), FilesFor(arrangement), tier, Ct);
    }

    // One arrangement per thing the analyzer reports once it checks: a broken rule's findings, the missing
    // model file, the ratcheted rule it could not check, and the model file it could not read.
    private static IReadOnlyList<AdditionalText> FilesFor(string arrangement)
    {
        return arrangement switch
        {
            "findings" => [BuildTier.ModelFile(BuildTier.ModelText(BuildTier.EnforceRule))],
            "no model file" => [],
            "unchecked rule" => [BuildTier.ModelFile(BuildTier.ModelText(BuildTier.MigrateRule))],
            "refused model" => [BuildTier.ModelFile("[]")],
            _ => throw new ArgumentOutOfRangeException(nameof(arrangement), arrangement, null)
        };
    }
}
