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
    public void SelectBestInstance_PrefersATestedMajorOverANewerUntestedOne()
    {
        // Arrange — VS 2022 (17.x) and a VS 19, newer than any tested major, both installed.
        VsInstance vs2022 = new(@"C:\VS\2022", new Version(17, 14, 100), "VS 2022");
        VsInstance vs19 = new(@"C:\VS\19", new Version(19, 0, 200), "VS 19");

        // Act — the untested one has the higher version, but the tested one should still win.
        VsInstance? selected = MsBuildBootstrap.SelectBestInstance([vs19, vs2022]);

        // Assert
        selected.ShouldBe(vs2022);
    }

    [Fact]
    public void SelectBestInstance_AmongTested_PicksHighestVersion()
    {
        // VS 2026 beside a VS 2022 is the pair a machine keeps through an upgrade, and the newer one wins.
        VsInstance vs2019 = new(@"C:\VS\2019", new Version(16, 11, 500), "VS 2019");
        VsInstance vs2022 = new(@"C:\VS\2022", new Version(17, 14, 100), "VS 2022");
        VsInstance vs2026 = new(@"C:\VS\18", new Version(18, 10, 100), "VS 2026");

        VsInstance? selected = MsBuildBootstrap.SelectBestInstance([vs2019, vs2026, vs2022]);

        selected.ShouldBe(vs2026);
    }

    [Fact]
    public void SelectBestInstance_NoTestedMajor_FallsBackToHighest()
    {
        // No 16, 17 or 18 installed; pick the highest among the newer ones.
        VsInstance vs19 = new(@"C:\VS\19", new Version(19, 4, 200), "VS 19");
        VsInstance vs26 = new(@"C:\VS\26", new Version(26, 0, 100), "VS 26");

        VsInstance? selected = MsBuildBootstrap.SelectBestInstance([vs19, vs26]);

        selected.ShouldBe(vs26);
    }

    [Theory]
    [InlineData(16, 11, "Visual Studio Professional 2019", "Visual Studio Professional 2019 (16.11, via vswhere)")]
    [InlineData(17, 14, "Visual Studio Professional 2022", "Visual Studio Professional 2022 (17.14, via vswhere)")]
    [InlineData(18, 10, "Visual Studio Community 2026", "Visual Studio Community 2026 (18.10, via vswhere)")]
    public void DescribeSelection_TestedMajor_ReportsAnOrdinaryPickWithNoCaveat(
        int major, int minor, string name, string expected)
    {
        // Arrange — the only Visual Studio on the machine, inside the tested envelope.
        VsInstance instance = new(@"C:\VS\only", new Version(major, minor, 100), name);

        // Act
        string description = MsBuildBootstrap.DescribeSelection(MsBuildBootstrap.SelectBestInstance([instance])!);

        // Assert — no caveat: an ordinary pick has to read as unremarkable, or the fallback below stops
        // reading as remarkable, and an agent passes the caveat on as a finding.
        description.ShouldBe(expected);
    }

    [Fact]
    public void DescribeSelection_NothingInsideTheEnvelope_NamesTheVersionAndSaysItFellOutside()
    {
        // Arrange — a machine whose only Visual Studio is newer than any tested major, so the highest
        // available is taken.
        VsInstance vs19 = new(@"C:\VS\19", new Version(19, 2, 11925), "Visual Studio Enterprise 19");

        // Act
        string description = MsBuildBootstrap.DescribeSelection(MsBuildBootstrap.SelectBestInstance([vs19])!);

        // Assert — the fallback arm names itself apart from a preferred pick, and names the version it fell
        // back to, which is the fact a load failure on such a machine turns on.
        description.ShouldBe(
            "Visual Studio Enterprise 19 (19.2, via vswhere) — outside the tested VS 2019/2022/2026 envelope");
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
