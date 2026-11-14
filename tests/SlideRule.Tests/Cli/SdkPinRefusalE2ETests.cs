using Shouldly;
using SlideRule.Roslyn.Hosting;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end refusals for a <c>global.json</c> pinning an SDK the .NET host cannot resolve, from the two places
///     the resolution happens: Roslyn's build host, which resolves from the directory of the project it starts for,
///     and MSBuild's registration in the CLI process, which resolves from the working directory. The surfaces'
///     parity on the build-host refusal is <c>CliMcpParityTests</c>' row.
/// </summary>
public sealed class SdkPinRefusalE2ETests
{
    [Fact]
    public async Task Graph_AProjectUnderItsOwnBrokenPin_NamesThatPinRatherThanTheSolutions()
    {
        // Arrange — the solution's own pin resolves on any machine with SDK 10 or later; only the project's fails,
        // and hostfxr's own text names the file it resolved.
        using TempDirectory bed = TestTempRoot.Fresh("sdk-pin");
        bed.WriteFile(["global.json"], """{ "sdk": { "version": "10.0.100", "rollForward": "latestMajor" } }""");
        string projectPin = bed.WriteFile(["B", "global.json"], ScratchProjects.BrokenSdkPin());
        bed.WriteFile(["B", "B.csproj"], ScratchProjects.ClassLibrary());
        string solution = bed.WriteFile(["App.slnx"], ScratchProjects.Solution("B/B.csproj"));

        // Act
        CliResult result = await CliRunner.InvokeColdAsync("graph", solution, "--no-cache");

        // Assert — the rest of the message quotes hostfxr, whose list of installed SDKs differs by machine.
        result.ShouldRefuseWith(
            $"The .NET host could not resolve an SDK for version {ScratchProjects.UnresolvableSdkVersion}, which "
            + $"'{projectPin}' pins, so the solution's projects cannot be loaded.");
    }

    [Fact]
    public async Task Graph_AFilterUnderABrokenPin_IsRefusedForTheSdkRatherThanAsAnUnreadableFilter()
    {
        // Arrange
        using TempDirectory bed = TestTempRoot.Fresh("sdk-pin");
        (string pin, _) = ScratchProjects.WriteBrokenPinBed(bed);
        string filter = bed.WriteFile(
            ["App.slnf"], """{ "solution": { "path": "App.slnx", "projects": [ "Lib/Lib.csproj" ] } }""");

        // Act
        CliResult result = await CliRunner.InvokeColdAsync("graph", filter, "--no-cache");

        // Assert
        result.ShouldRefuseWith(
            $"The .NET host could not resolve an SDK for version {ScratchProjects.UnresolvableSdkVersion}, which "
            + $"'{pin}' pins, so the solution's projects cannot be loaded.");
    }

    [Fact]
    public async Task Graph_StartedUnderABrokenPin_RefusesBeforeMsBuildRegisters()
    {
        Assert.SkipUnless(
            MsBuildBootstrap.LastSelection?.StartsWith("MSBuildLocator default", StringComparison.Ordinal) == true,
            "Registration resolves an SDK from the working directory only when no Visual Studio MSBuild is "
            + "selected; with one selected, a failed resolution falls back to Visual Studio's MSBuild.");

        // Arrange — the process starts under the pin, and the solution lies outside it.
        using TempDirectory pinned = TestTempRoot.Fresh("sdk-pin");
        string pin = pinned.WriteFile(["global.json"], ScratchProjects.BrokenSdkPin(ScratchProjects.PinInstruction));
        using TempDirectory elsewhere = TestTempRoot.Fresh("sdk-pin");
        elsewhere.WriteFile(["Lib", "Lib.csproj"], ScratchProjects.ClassLibrary());
        string solution = elsewhere.WriteFile(["App.slnx"], ScratchProjects.Solution("Lib/Lib.csproj"));

        // Act
        ChildProcess.ProcessResult result = await ChildProcess.RunAsync(
            DotnetCommand.StartInfo([TestsBinCli.Dll(), "graph", solution, "--no-cache"], pinned.Path),
            TimeSpan.FromMinutes(3),
            ct: Ct);

        // Assert
        string transcript = result.Transcript();
        result.ExitCode.ShouldBe(2, transcript);
        result.StandardError.NormalizedTrimmed()
            .ShouldBe(
                $"The .NET host could not resolve an SDK for version {ScratchProjects.UnresolvableSdkVersion}, which "
                + $"'{pin}' pins, so MSBuild cannot be registered.\n"
                + "The .NET host said:\n"
                + $"  {ScratchProjects.PinInstruction}\n"
                + "Follow the repository's instruction above; otherwise, install SDK "
                + $"{ScratchProjects.UnresolvableSdkVersion}, or update '{pin}' to name an installed SDK.\n"
                + "If the SDK is installed under another dotnet root, put that root first on PATH.\n"
                + $"Running from a directory '{pin}' does not cover, passing the solution's absolute path, avoids "
                + "that pin.",
                transcript);
    }
}
