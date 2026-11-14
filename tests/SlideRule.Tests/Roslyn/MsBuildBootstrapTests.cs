using Shouldly;
using SlideRule.Roslyn.Hosting;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="MsBuildBootstrap.SelectBestInstance" /> is pure and needs no MSBuild runtime. The
///     <c>Initialize</c> tests drive the override arm in a process the module initializer has already
///     registered: its throw-paths refuse BEFORE any <c>MSBuildLocator</c> registration call, and the
///     VSToolsPath tests read what the arm set before its second registration is refused. Env vars are scoped
///     to the test that sets them and the collection is serial.
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
    [InlineData(null, "MSBuildLocator default (the .NET SDK's MSBuild)")]
    [InlineData("vswhere did not answer within 10 s",
        "MSBuildLocator default (no Visual Studio install detected via vswhere; vswhere did not answer within 10 s)")]
    public void DescribeDefaultFallback_CarriesVswheresReasonWhenThereIsOne(string? whyNone, string expected)
    {
        // The bare form is what a host that never asks vswhere reports, so it claims no probe, and ci.yml
        // matches it exactly there; on Windows the reason is what tells a missing vswhere from a wedged or
        // failing one.
        MsBuildBootstrap.DescribeDefaultFallback(whyNone)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("VS 2026 (18.10, via vswhere)", true,
        @"MSBuild for this run: VS 2026 (18.10, via vswhere). Set SLIDERULE_VS_INSTALL_PATH to a Visual Studio install root (the parent of MSBuild\Current\Bin) to select a different MSBuild.")]
    [InlineData("MSBuildLocator default (the .NET SDK's MSBuild)", false,
        "MSBuild for this run: MSBuildLocator default (the .NET SDK's MSBuild).")]
    [InlineData(null, false, "MSBuild for this run: not registered by this process.")]
    public void ComposeSelectionNote_OffersTheVisualStudioOverrideOnlyWhereOneExists(
        string? selection, bool offersVsOverride, string expected)
    {
        // A host with no Visual Studio has no install root to name, so a remedy there would send the reader
        // to a layout that does not exist.
        MsBuildBootstrap.ComposeSelectionNote(selection, offersVsOverride)
            .ShouldBe(expected);
    }

    [Fact]
    public void SelectionNote_OffersTheOverrideOnlyOnWindows()
    {
        string note = MsBuildBootstrap.SelectionNote();

        note.Contains(SlideRuleEnvVars.VsInstallPath)
            .ShouldBe(OperatingSystem.IsWindows(), customMessage: note);
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

    // The VSToolsPath facts below read the environment the override arm leaves behind. The layout is Windows paths,
    // as every Visual Studio install root is.
    [Fact]
    public void Initialize_OverrideRootWithVsTargets_PointsVsToolsPathAtTheHighestVersion()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A Visual Studio install root is a Windows layout.");
        using TempDirectory vsRoot = FakeVsRoot();
        Directory.CreateDirectory(vsRoot.PathOf("MSBuild", "Microsoft", "VisualStudio", "v17.0"));
        Directory.CreateDirectory(vsRoot.PathOf("MSBuild", "Microsoft", "VisualStudio", "v18.0"));
        Directory.CreateDirectory(vsRoot.PathOf("MSBuild", "Microsoft", "VisualStudio", "Managed"));

        string? vsToolsPath = ShouldRefuseTheOverrideArmsSecondRegistration(vsRoot);

        vsToolsPath.ShouldBe(vsRoot.PathOf("MSBuild", "Microsoft", "VisualStudio", "v18.0"));
    }

    [Fact]
    public void Initialize_OverrideRootWithoutVsTargets_LeavesVsToolsPathUnset()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A Visual Studio install root is a Windows layout.");
        using TempDirectory vsRoot = FakeVsRoot();

        string? vsToolsPath = ShouldRefuseTheOverrideArmsSecondRegistration(vsRoot);

        vsToolsPath.ShouldBeNull();
    }

    // Runs Initialize's override arm at vsRoot, which applies VSToolsPath and then fails registering a second
    // engine in this process, and returns the VSToolsPath it left. Every variable the arm writes is scoped, so the
    // process gets its own registration's values back; the refusal is the second registration's, not a probe of
    // the targets directory.
    private static string? ShouldRefuseTheOverrideArmsSecondRegistration(TempDirectory vsRoot)
    {
        using var vsInstall = new ScopedEnvironmentVariable(SlideRuleEnvVars.VsInstallPath, vsRoot.Path);
        using var vsToolsPath = new ScopedEnvironmentVariable("VSToolsPath", null);
        using var vsInstallDir = new ScopedEnvironmentVariable("VSINSTALLDIR", null);
        using var vsCmdVer = new ScopedEnvironmentVariable("VSCMD_VER", null);

        Should.Throw<InvalidOperationException>(() => MsBuildBootstrap.Initialize());

        return Environment.GetEnvironmentVariable("VSToolsPath");
    }

    // An install root carrying the one file the override arm probes for.
    private static TempDirectory FakeVsRoot()
    {
        TempDirectory vsRoot = TestTempRoot.Fresh("msbuild-bootstrap");
        vsRoot.WriteFile(["MSBuild", "Current", "Bin", "MSBuild.exe"], "");
        return vsRoot;
    }
}
