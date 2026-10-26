using Shouldly;
using SlideRule.Roslyn.Hosting;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="MsBuildBootstrap.SelectBestInstance" /> is pure and needs no MSBuild runtime. The
///     two <c>Initialize</c> tests exercise the override throw-paths, which run BEFORE any
///     <c>MSBuildLocator</c> registration call — safe even though the module initializer has already
///     registered MSBuild. Env vars are scoped to the test that sets them and the collection is serial.
/// </summary>
[Collection("Serial")]
public sealed class MsBuildBootstrapTests
{
    [Fact]
    public void SelectBestInstance_EmptyList_ReturnsNull()
    {
        VsInstance? result = MsBuildBootstrap.SelectBestInstance([]);
        result.ShouldBeNull();
    }

    [Fact]
    public void SelectBestInstance_PrefersStableOverPreview()
    {
        // Arrange — VS 2022 (17.x) and a preview VS 18 both installed.
        VsInstance vs2022 = new(@"C:\VS\2022", new Version(17, 14, 100), "VS 2022");
        VsInstance vs18Preview = new(@"C:\VS\18", new Version(18, 4, 200), "VS 18");

        // Act — preview has higher version but stable should still win.
        VsInstance? selected = MsBuildBootstrap.SelectBestInstance([vs18Preview, vs2022]);

        // Assert
        selected.ShouldBe(vs2022);
    }

    [Fact]
    public void SelectBestInstance_AmongStable_PicksHighestVersion()
    {
        VsInstance vs2019 = new(@"C:\VS\2019", new Version(16, 11, 500), "VS 2019");
        VsInstance vs2022Old = new(@"C:\VS\2022old", new Version(17, 0, 0), "VS 2022");
        VsInstance vs2022New = new(@"C:\VS\2022new", new Version(17, 14, 100), "VS 2022");

        VsInstance? selected = MsBuildBootstrap.SelectBestInstance([vs2019, vs2022Old, vs2022New]);

        selected.ShouldBe(vs2022New);
    }

    [Fact]
    public void SelectBestInstance_OnlyPreview_FallsBackToHighest()
    {
        // No 16/17 installed; pick highest among newer.
        VsInstance vs18 = new(@"C:\VS\18", new Version(18, 4, 200), "VS 18");
        VsInstance vs26 = new(@"C:\VS\26", new Version(26, 0, 100), "VS 26");

        VsInstance? selected = MsBuildBootstrap.SelectBestInstance([vs18, vs26]);

        selected.ShouldBe(vs26);
    }

    [Fact]
    public void DescribeSelection_PreferredMajor_ReportsAnOrdinaryPickWithNoCaveat()
    {
        // Arrange — a VS 2022, which is inside the tested envelope.
        VsInstance vs2022 = new(@"C:\VS\2022", new Version(17, 14, 100), "Visual Studio Professional 2022");

        // Act
        string description = MsBuildBootstrap.DescribeSelection(MsBuildBootstrap.SelectBestInstance([vs2022])!);

        // Assert — no caveat: an ordinary pick has to read as unremarkable, or the fallback below stops
        // reading as remarkable.
        description.ShouldBe("Visual Studio Professional 2022 (17.14, via vswhere)");
    }

    [Fact]
    public void DescribeSelection_NothingInsideTheEnvelope_NamesTheVersionAndSaysItFellOutside()
    {
        // Arrange — the shape of a CI image that ships VS 2026 alone: nothing for the 16/17 preference to
        // pick, so the highest available is taken.
        VsInstance vs2026 = new(@"C:\VS\18", new Version(18, 7, 11925), "Visual Studio Enterprise 2026");

        // Act
        string description = MsBuildBootstrap.DescribeSelection(MsBuildBootstrap.SelectBestInstance([vs2026])!);

        // Assert — the fallback arm names itself apart from a preferred pick, and names the version it fell
        // back to, which is the fact a load failure on such a machine turns on.
        description.ShouldBe(
            "Visual Studio Enterprise 2026 (18.7, via vswhere) — outside the tested VS 2019/2022 envelope");
    }

    [Theory]
    [InlineData(null, "MSBuildLocator default (no Visual Studio install detected via vswhere)")]
    [InlineData("vswhere did not answer within 10 s",
        "MSBuildLocator default (no Visual Studio install detected via vswhere; vswhere did not answer within 10 s)")]
    public void DescribeDefaultFallback_CarriesVswheresReasonWhenThereIsOne(string? whyNone, string expected)
    {
        // The bare form is what a host that never asks vswhere reports, and ci.yml matches it exactly there;
        // on Windows the reason is what tells a missing vswhere from a wedged or failing one.
        MsBuildBootstrap.DescribeDefaultFallback(whyNone)
            .ShouldBe(expected);
    }

    [Fact]
    public void DescribeDefaultFallback_NeverReadsAsAVisualStudioPick()
    {
        // ci.yml tells a Visual Studio pick from the fallback by the ', via vswhere)' that closes every
        // DescribeSelection; the fallback's own 'via vswhere' must never be followed by that comma-led tail.
        MsBuildBootstrap.DescribeDefaultFallback("vswhere listed no instances")
            .ShouldNotContain(", via vswhere)");
        MsBuildBootstrap.DescribeDefaultFallback(null)
            .ShouldNotContain(", via vswhere)");
    }

    [Fact]
    public void LastSelection_AfterRegistration_IsPublished()
    {
        // The module initializer registered MSBuild through MsBuildBootstrap before any test ran, so the
        // published selection is the one this process is actually running on. This is the pin that keeps
        // the description reachable: DescribeSelection could be perfect and still never leave the class.
        MsBuildBootstrap.LastSelection.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Initialize_OverrideEnvVar_NonExistentDir_Throws()
    {
        using var overridden = new ScopedEnvironmentVariable(
            SlideRuleEnvVars.VsInstallPath, @"C:\__does_not_exist_for_test__");

        Action act = () => MsBuildBootstrap.Initialize();

        var ex = act.ShouldThrow<InvalidOperationException>();
        ex.Message.ShouldContain(SlideRuleEnvVars.VsInstallPath);
        ex.Message.ShouldContain("not an existing directory");
    }

    [Fact]
    public void Initialize_OverrideEnvVar_DirWithoutMsBuild_Throws()
    {
        using TempDirectory temp = TestTempRoot.Fresh("msbuild-bootstrap");
        using var overridden = new ScopedEnvironmentVariable(SlideRuleEnvVars.VsInstallPath, temp.Path);

        Action act = () => MsBuildBootstrap.Initialize();

        var ex = act.ShouldThrow<InvalidOperationException>();
        ex.Message.ShouldContain("MSBuild.exe not found");
    }
}
