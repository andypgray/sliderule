using Shouldly;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="SdkResolutionRefusal" />'s recognition of hostfxr's failure text and the refusal it words, each
///     shape pinned without a broken SDK. The end-to-end routes are <c>SdkPinRefusalE2ETests</c> and the
///     <c>arch_graph</c> parity row.
/// </summary>
public sealed class SdkResolutionRefusalTests
{
    // Roslyn's build host's text for a pin that supplies its own errorMessage, here the restore instruction.
    private const string EfCoreMessage =
        "An exception of type System.InvalidOperationException was thrown: Error while calling hostfxr function "
        + "hostfxr_resolve_sdk2. Error code: -2147450725 Detailed error: The required .NET SDK wasn't found. "
        + @"Please run ./restore.sh or .\restore.cmd to install it.";

    // hostfxr's own text for a pin with no errorMessage.
    private const string DefaultDetail =
        "A compatible .NET SDK was not found.\n\nRequested SDK version: 11.0.100-rc.2.26504.105\n"
        + @"global.json file: C:\src\App\global.json" + "\n\nInstalled SDKs:\n\n"
        + "Learn about SDK resolution:\nhttps://aka.ms/dotnet/sdk-not-found";

    private const string Instruction = "Run ./bootstrap.sh to install the pinned SDK.";

    [Fact]
    public void DetailOf_TheBuildHostsWrappedText_IsEverythingAfterTheMarker()
    {
        SdkResolutionRefusal.DetailOf(new InvalidOperationException(EfCoreMessage))
            .ShouldBe(@"The required .NET SDK wasn't found. Please run ./restore.sh or .\restore.cmd to install it.");
    }

    [Fact]
    public void DetailOf_MsBuildLocatorsOwnTextWithHostfxrsDefault_IsTheWholeDetailTrimmed()
    {
        InvalidOperationException thrown = HostfxrFailure(DefaultDetail + "\n");

        SdkResolutionRefusal.DetailOf(thrown)
            .ShouldBe(DefaultDetail);
    }

    [Fact]
    public void DetailOf_NoDetailMarker_IsTheWholeMessage()
    {
        SdkResolutionRefusal.DetailOf(new InvalidOperationException(" hostfxr_resolve_sdk2 failed "))
            .ShouldBe("hostfxr_resolve_sdk2 failed");
    }

    [Fact]
    public void DetailOf_AnUnrelatedFailure_IsNull()
    {
        SdkResolutionRefusal.DetailOf(new InvalidOperationException("No instances of MSBuild could be detected."))
            .ShouldBeNull();
    }

    [Fact]
    public void ForLoadAndForRegistration_EachRefusal_IsAnSdkRefusalWrappingTheFailure()
    {
        var failure = new InvalidOperationException(EfCoreMessage);
        string solution = Path.Combine(Path.GetTempPath(), "App.slnx");

        // Declared as the base type, so a factory that returns a plain refusal still compiles and reds here.
        Exception forLoad = SdkResolutionRefusal.ForLoad(solution, failure);
        Exception forRegistration = SdkResolutionRefusal.ForRegistration(Path.GetTempPath(), failure);

        forLoad.ShouldBeOfType<SdkRefusalException>()
            .InnerException
            .ShouldBeSameAs(failure);
        forRegistration.ShouldBeOfType<SdkRefusalException>()
            .InnerException
            .ShouldBeSameAs(failure);
    }

    [Fact]
    public void ForLoad_AProjectPinWithPathsAndAnInstructionButNoVersion_NamesThatPinAloneWithoutThePathAdvice()
    {
        // Arrange — hostfxr honours sdk.paths and sdk.errorMessage with no sdk.version, so the versionless nested
        // pin is the one that failed, and its instruction in the detail is what says so.
        using TempDirectory temp = TestTempRoot.Fresh("sdk-refusal");
        temp.WriteFile(["global.json"], """{ "sdk": { "version": "10.0.100", "rollForward": "latestMajor" } }""");
        string projectPin = temp.WriteFile(
            ["B", "global.json"], $$"""{ "sdk": { "paths": [ ".missing" ], "errorMessage": "{{Instruction}}" } }""");
        temp.WriteFile(["B", "B.csproj"], ScratchProjects.ClassLibrary());
        string solution = temp.WriteFile(["App.slnx"], ScratchProjects.Solution("B/B.csproj"));
        // Act
        UserErrorException refusal = SdkResolutionRefusal.ForLoad(solution, HostfxrFailure(Instruction));

        // Assert
        refusal.Message.ShouldBe(
            $"The .NET host could not resolve an SDK for the pin in '{projectPin}', so the solution's projects cannot "
            + "be loaded.\n"
            + "The .NET host said:\n"
            + $"  {Instruction}\n"
            + $"Follow the repository's instruction above; otherwise, install the SDK '{projectPin}' pins, or update "
            + "it to name an installed SDK.\n"
            + "Running from another directory does not help: each project resolves its SDK from its own "
            + "directory.");
    }

    [Fact]
    public void ForLoad_NoPinQuotedAndAProjectFileSettingNoSdkValue_NamesTheVersionedPinAlone()
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("sdk-refusal");
        string solutionPin = temp.WriteFile(["global.json"], """{ "sdk": { "version": "98.0.100" } }""");
        temp.WriteFile(["B", "global.json"], """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");
        temp.WriteFile(["B", "B.csproj"], ScratchProjects.ClassLibrary());
        string solution = temp.WriteFile(["App.slnx"], ScratchProjects.Solution("B/B.csproj"));

        // Act
        UserErrorException refusal = SdkResolutionRefusal.ForLoad(solution, HostfxrFailure("The SDK was not found."));

        // Assert
        refusal.Message.ShouldStartWith(
            $"The .NET host could not resolve an SDK for version 98.0.100, which '{solutionPin}' pins, so the "
            + "solution's projects cannot be loaded.\n");
    }

    [Fact]
    public void ForLoad_ASolutionThatDoesNotParse_StillNamesThePinOverItsDirectory()
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("sdk-refusal");
        string solutionPin = temp.WriteFile(["global.json"], """{ "sdk": { "version": "98.0.100" } }""");
        string solution = temp.WriteFile(["App.slnx"], "<Solution>\n  <Project Path=\"A/A.csproj\"\n");

        // Act
        UserErrorException refusal = SdkResolutionRefusal.ForLoad(solution, HostfxrFailure("The SDK was not found."));

        // Assert
        refusal.Message.ShouldStartWith(
            $"The .NET host could not resolve an SDK for version 98.0.100, which '{solutionPin}' pins, so the "
            + "solution's projects cannot be loaded.\n");
    }

    [Fact]
    public void HostfxrLines_HostfxrsDefaultText_ReadsTheVersionAndTheFile()
    {
        SdkResolutionRefusal.HostfxrLines(DefaultDetail)
            .ShouldBe(("11.0.100-rc.2.26504.105", @"C:\src\App\global.json"));
    }

    [Fact]
    public void HostfxrLines_APinsOwnErrorMessage_ReadsNeither()
    {
        SdkResolutionRefusal.HostfxrLines(Instruction)
            .ShouldBe((null, null));
    }

    [Fact]
    public void Message_OnePinWithItsOwnInstruction_LeadsWithTheInstruction()
    {
        GlobalJsonPin pin = new("/repo/global.json", "99.0.100", "disable", HasPaths: false, Instruction);

        SdkResolutionRefusal.Message(SdkRefusalTrigger.BuildHost, [pin], Instruction)
            .ShouldBe(
                "The .NET host could not resolve an SDK for version 99.0.100, which '/repo/global.json' pins, so the "
                + "solution's projects cannot be loaded.\n"
                + "The .NET host said:\n"
                + "  Run ./bootstrap.sh to install the pinned SDK.\n"
                + "Follow the repository's instruction above; otherwise, install SDK 99.0.100, or update "
                + "'/repo/global.json' to name an installed SDK.\n"
                + "If the SDK is installed under another dotnet root, put that root first on PATH.\n"
                + "Running from another directory does not help: each project resolves its SDK from its own "
                + "directory.");
    }

    [Fact]
    public void Message_OnePinWithHostfxrsDefaultText_QuotesTheHostIndentedAndOffersTheInstall()
    {
        GlobalJsonPin pin = new(@"C:\src\App\global.json", "11.0.100-rc.2.26504.105", null, HasPaths: false,
            null);

        SdkResolutionRefusal.Message(SdkRefusalTrigger.BuildHost, [pin], DefaultDetail)
            .ShouldBe(
                "The .NET host could not resolve an SDK for version 11.0.100-rc.2.26504.105, which "
                + @"'C:\src\App\global.json' pins, so the solution's projects cannot be loaded." + "\n"
                + "The .NET host said:\n"
                + "  A compatible .NET SDK was not found.\n"
                + "\n"
                + "  Requested SDK version: 11.0.100-rc.2.26504.105\n"
                + @"  global.json file: C:\src\App\global.json" + "\n"
                + "\n"
                + "  Installed SDKs:\n"
                + "\n"
                + "  Learn about SDK resolution:\n"
                + "  https://aka.ms/dotnet/sdk-not-found\n"
                + @"Install SDK 11.0.100-rc.2.26504.105, or update 'C:\src\App\global.json' to name an "
                + "installed SDK.\n"
                + "If the SDK is installed under another dotnet root, put that root first on PATH.\n"
                + "Running from another directory does not help: each project resolves its SDK from its own "
                + "directory.");
    }

    [Fact]
    public void Message_SeveralPins_ListsEachFileWithItsVersion()
    {
        GlobalJsonPin solution = new("/repo/global.json", "10.0.100", "latestMajor", HasPaths: false, null);
        GlobalJsonPin project = new("/repo/src/B/global.json", "99.0.100", null, HasPaths: false, Instruction);
        GlobalJsonPin unreadable = new("/repo/src/C/global.json", null, null, HasPaths: false, null);

        SdkResolutionRefusal.Message(SdkRefusalTrigger.BuildHost, [solution, project, unreadable], Instruction)
            .ShouldBe(
                "The .NET host could not resolve an SDK for one of the pins below, so the solution's projects cannot "
                + "be loaded:\n"
                + "  '/repo/global.json' pins 10.0.100\n"
                + "  '/repo/src/B/global.json' pins 99.0.100\n"
                + "  '/repo/src/C/global.json', which names no readable SDK version\n"
                + "The .NET host said:\n"
                + "  Run ./bootstrap.sh to install the pinned SDK.\n"
                + "Follow the repository's instruction above; otherwise, install the SDK each pin names, or update "
                + "those files to name installed SDKs.\n"
                + "If the SDK is installed under another dotnet root, put that root first on PATH.\n"
                + "Running from another directory does not help: each project resolves its SDK from its own "
                + "directory.");
    }

    [Fact]
    public void Message_NoPinFound_NamesNoFile()
    {
        SdkResolutionRefusal.Message(SdkRefusalTrigger.BuildHost, [], "The required .NET SDK wasn't found.")
            .ShouldBe(
                "The .NET host could not resolve an SDK for this solution's projects, so the solution's projects "
                + "cannot be loaded.\n"
                + "The .NET host said:\n"
                + "  The required .NET SDK wasn't found.\n"
                + "Install the SDK the host asked for, or update the global.json that pins it.\n"
                + "If the SDK is installed under another dotnet root, put that root first on PATH.\n"
                + "Running from another directory does not help: each project resolves its SDK from its own "
                + "directory.");
    }

    [Fact]
    public void Message_APinWithPathsAndNoReadableVersion_DropsThePathAlternative()
    {
        GlobalJsonPin pin = new("/repo/global.json", null, null, HasPaths: true, null);

        SdkResolutionRefusal.Message(SdkRefusalTrigger.BuildHost, [pin], "The required .NET SDK wasn't found.")
            .ShouldBe(
                "The .NET host could not resolve an SDK for the pin in '/repo/global.json', so the solution's "
                + "projects cannot be loaded.\n"
                + "The .NET host said:\n"
                + "  The required .NET SDK wasn't found.\n"
                + "Install the SDK '/repo/global.json' pins, or update it to name an installed SDK.\n"
                + "Running from another directory does not help: each project resolves its SDK from its own "
                + "directory.");
    }

    [Fact]
    public void Message_InProcessUnderOnePin_SaysMsBuildCannotRegisterAndThatRunningElsewhereAvoidsIt()
    {
        GlobalJsonPin pin = new("/repo/global.json", "99.0.100", "disable", HasPaths: false, Instruction);

        SdkResolutionRefusal.Message(SdkRefusalTrigger.InProcess, [pin], Instruction)
            .ShouldBe(
                "The .NET host could not resolve an SDK for version 99.0.100, which '/repo/global.json' pins, so "
                + "MSBuild cannot be registered.\n"
                + "The .NET host said:\n"
                + "  Run ./bootstrap.sh to install the pinned SDK.\n"
                + "Follow the repository's instruction above; otherwise, install SDK 99.0.100, or update "
                + "'/repo/global.json' to name an installed SDK.\n"
                + "If the SDK is installed under another dotnet root, put that root first on PATH.\n"
                + "Running from a directory '/repo/global.json' does not cover, passing the solution's absolute "
                + "path, avoids that pin.");
    }

    [Fact]
    public void Message_InProcessWithNoPin_NamesTheWorkingDirectory()
    {
        SdkResolutionRefusal.Message(SdkRefusalTrigger.InProcess, [], "The required .NET SDK wasn't found.")
            .ShouldBe(
                "The .NET host could not resolve an SDK for this process's working directory, so MSBuild cannot be "
                + "registered.\n"
                + "The .NET host said:\n"
                + "  The required .NET SDK wasn't found.\n"
                + "Install the SDK the host asked for, or update the global.json that pins it.\n"
                + "If the SDK is installed under another dotnet root, put that root first on PATH.\n"
                + "Running from another directory with the solution's absolute path may avoid it.");
    }

    // MSBuildLocator's failure text with hostfxr's detail, as the build host forwards it.
    private static InvalidOperationException HostfxrFailure(string detail)
    {
        return new InvalidOperationException(
            "Error while calling hostfxr function hostfxr_resolve_sdk2. Error code: -2147450725 Detailed error: "
            + detail);
    }
}
