using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Roslyn.Replay;
using Xunit;

namespace SlideRule.Tests.Replay;

/// <summary>
///     The hermetic corners of <see cref="BinlogReplayer" /> — the ones that never reach MSBuild: the
///     blank-path guard that throws on the first line before any reader is opened, and
///     <see cref="BinlogReplayer.EnsureAssemblyExtension" />'s OutputKind → assembly-extension mapping (a pure
///     string function, exposed <c>internal</c> for this test). The full replay path is exercised end-to-end by
///     <c>BinlogReplayFidelityTests</c>; these need no binlog fixture.
/// </summary>
public sealed class BinlogReplayerUnitTests
{
    [Fact]
    public async Task Replay_BlankPath_ThrowsArgumentExceptionBeforeAnyBuild()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace is the very first statement — no SolutionReader, no MSBuild.
        await Should.ThrowAsync<ArgumentException>(() => BinlogReplayer.ReplayAsync("", solutionPath: null));
    }

    [Theory]
    // No assembly extension yet: the OutputKind decides which one is appended.
    [InlineData("MyApp.Domain", OutputKind.DynamicallyLinkedLibrary, "MyApp.Domain.dll")]
    [InlineData("MyApp.Domain", null, "MyApp.Domain.dll")]
    [InlineData("MyApp.Cli", OutputKind.ConsoleApplication, "MyApp.Cli.exe")]
    [InlineData("MyApp.WinForms", OutputKind.WindowsApplication, "MyApp.WinForms.exe")]
    [InlineData("MyApp.WinRt", OutputKind.WindowsRuntimeApplication, "MyApp.WinRt.exe")]
    [InlineData("MyApp.Mod", OutputKind.NetModule, "MyApp.Mod.netmodule")]
    [InlineData("MyApp.Component", OutputKind.WindowsRuntimeMetadata, "MyApp.Component.winmdobj")]
    // A dotted assembly name is the whole reason the guard tests known extensions explicitly instead of using
    // Path.GetExtension: ".Billing" is not an assembly extension, so the real one is still appended.
    [InlineData("MyApp.Legacy.Billing", OutputKind.DynamicallyLinkedLibrary, "MyApp.Legacy.Billing.dll")]
    // Already carries a known assembly extension (case-insensitively): early return, the OutputKind is ignored.
    [InlineData("MyApp.Domain.dll", OutputKind.ConsoleApplication, "MyApp.Domain.dll")]
    [InlineData("tool.EXE", OutputKind.DynamicallyLinkedLibrary, "tool.EXE")]
    public void EnsureAssemblyExtension_AppendsKindExtension_UnlessOneIsAlreadyPresent(
        string outputPath, OutputKind? outputKind, string expected)
    {
        BinlogReplayer.EnsureAssemblyExtension(outputPath, outputKind)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("/langversion:99", "99")]
    [InlineData("-langversion:12", "12")]
    [InlineData("/LangVersion:\"latest\"", "latest")]
    // The compiler takes the last one, whichever form each is written in.
    [InlineData("/langversion:7.3|-langversion:preview", "preview")]
    [InlineData("-langversion:preview|/langversion:7.3", "7.3")]
    [InlineData("/nullable:enable|/langversionx", null)]
    [InlineData("", null)]
    public void LangVersionOf_IsTheLastLangVersionOption(string arguments, string? expected)
    {
        string[] split = arguments.Length == 0 ? [] : arguments.Split('|');

        RecordedCompiles.LangVersionOf(split).ShouldBe(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ByProject_AProjectCompiledPerFramework_PairsEachFrameworkWithItsOwnCall(bool reversed)
    {
        // Whichever order the build recorded the calls in, each framework's project takes its own.
        string projectFile = Path.GetFullPath("/repo/App/App.csproj");
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.CurrentSolution;
        var older = ProjectId.CreateNewId();
        var newer = ProjectId.CreateNewId();
        solution = solution
            .AddProject(ProjectInfo.Create(older, VersionStamp.Default, "App", "App", LanguageNames.CSharp, projectFile))
            .AddProject(ProjectInfo.Create(newer, VersionStamp.Default, "App", "App", LanguageNames.CSharp, projectFile));
        var frameworks = new Dictionary<ProjectId, string> { [older] = "netstandard2.0", [newer] = "net10.0" };
        RecordedCompile olderCall = new(projectFile, "netstandard2.0", "98", null);
        RecordedCompile newerCall = new(projectFile, "net10.0", "14.0", null);
        RecordedCompile[] recorded = reversed ? [newerCall, olderCall] : [olderCall, newerCall];

        IReadOnlyDictionary<ProjectId, RecordedCompile> paired = RecordedCompiles.ByProject(solution, frameworks, recorded);

        paired.ShouldSatisfyAllConditions(
            () => paired.GetValueOrDefault(older).ShouldBe(olderCall),
            () => paired.GetValueOrDefault(newer).ShouldBe(newerCall));
    }

    [Fact]
    public void ByProject_AProjectWithOneCall_TakesItWithNoFramework()
    {
        string projectFile = Path.GetFullPath("/repo/App/App.csproj");
        using var workspace = new AdhocWorkspace();
        var id = ProjectId.CreateNewId();
        Solution solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(id, VersionStamp.Default, "App", "App", LanguageNames.CSharp, projectFile));
        RecordedCompile call = new(projectFile, "net10.0", "14.0", null);

        RecordedCompiles.ByProject(solution, new Dictionary<ProjectId, string>(), [call])
            .GetValueOrDefault(id)
            .ShouldBe(call);
    }
}
