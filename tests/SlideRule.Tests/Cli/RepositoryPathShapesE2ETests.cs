using System.Text.Json;
using Microsoft.Win32;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end <c>graph</c> over solutions whose paths take shapes real repositories carry and no fixture does:
///     project, folder, namespace and file names outside ASCII, and a source file whose full path passes the
///     260-character Windows limit from any clone root.
/// </summary>
public sealed class RepositoryPathShapesE2ETests
{
    [Fact]
    public async Task GraphJson_NamesOutsideAscii_ReachTheModelAndTheDocumentAsWritten()
    {
        // Arrange
        using TempDirectory bed = TestTempRoot.Fresh("non-ascii-paths");
        bed.WriteFile(["Café.Core", "Café.Core.csproj"], ScratchProjects.ClassLibrary());
        bed.WriteFile(["Café.Core", "Überweisung.cs"], "namespace Café.Core;\n\npublic sealed class Überweisung;\n");
        string solution = bed.WriteFile(["App.slnx"], ScratchProjects.Solution("Café.Core/Café.Core.csproj"));
        DotnetCli.Run("restore App.slnx", bed.Path);

        // Act
        CliResult result = await CliRunner.InvokeColdAsync("graph", solution, "--no-cache", "--json");

        // Assert — the project and its one type are surveyed, and the document spells the name as the solution
        // does rather than as a JSON escape, which is what a reader searching the output for it would type.
        result.ShouldSucceed("\"name\": \"Café.Core\"");
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.SurveyProject("Café.Core")
            .GetProperty("types")
            .GetInt32()
            .ShouldBe(1, result.Transcript());
    }

    [Fact]
    public async Task GraphJson_SourcePathPastTheWindowsLimit_ReachesTheModel()
    {
        Assert.SkipUnless(
            !OperatingSystem.IsWindows() || LongPathsEnabled(),
            "Windows refuses a path past 260 characters unless LongPathsEnabled is set, for every tool alike.");

        // Arrange — the file sits deep enough below the clone root that its full path passes 260 characters.
        using TempDirectory bed = TestTempRoot.Fresh("long-paths");
        bed.WriteFile(["Deep", "Deep.csproj"], ScratchProjects.ClassLibrary());
        string[] segments = ["Deep", .. Enumerable.Repeat("SegmentOfAReasonablyLongFolderName", 8), "Faraway.cs"];
        string source = bed.WriteFile(segments, "namespace Deep;\n\npublic sealed class Faraway;\n");
        source.Length.ShouldBeGreaterThan(260, "the arrange must put the file past the limit");
        string solution = bed.WriteFile(["App.slnx"], ScratchProjects.Solution("Deep/Deep.csproj"));
        DotnetCli.Run("restore App.slnx", bed.Path);

        // Act
        CliResult result = await CliRunner.InvokeColdAsync("graph", solution, "--no-cache", "--json");

        // Assert
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.SurveyProject("Deep")
            .GetProperty("types")
            .GetInt32()
            .ShouldBe(1, result.Transcript());
    }

    private static bool LongPathsEnabled()
    {
        // The registry is Windows-only, and the skip gate reads it only after OperatingSystem.IsWindows() is true,
        // a guard the platform analyzer cannot see from inside this method.
#pragma warning disable CA1416
        return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 0)
            is 1;
#pragma warning restore CA1416
    }
}
