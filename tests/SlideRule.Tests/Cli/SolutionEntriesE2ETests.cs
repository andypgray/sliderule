using System.Text.Json;
using Shouldly;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.Replay;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end <c>graph</c> and <c>check</c> over a solution declaring entries its default configuration does
///     not build: two C# projects it maps with no <c>Build.0</c> line, one of which the built project references,
///     and two Web Site entries, one with a URL path and one with a folder path.
/// </summary>
/// <remarks>
///     <para>
///         The workspace loads every C# entry whatever the configuration says, while a restore of the solution
///         follows the configuration and skips a project it does not build. Read naively, that project is an
///         unrestored one and the run refuses with advice to run the restore that skipped it. These pin that it is
///         left out of the model as the build leaves it out, and named as the reason the survey is narrower than
///         the solution. The not-built project the built one references leaves the model too: a restore of the
///         solution restores it on that project's behalf, but a build neither builds it nor passes its assembly on,
///         so a load that kept it would describe a larger solution than a replay of the build.
///     </para>
///     <para>
///         The workspace reports a Web Site entry as a project file it could not find, at a path made by joining
///         the entry's URL or folder onto the solution directory. These pin that the entry is named as unsupported
///         by its solution name and that the made-up path appears nowhere.
///     </para>
/// </remarks>
public sealed class SolutionEntriesE2ETests
{
    private const string Bed = "RestoreFailureSolutions/ConfigExcluded";

    private const string Solution = "ConfigExcluded.sln";

    private const string SpecSource =
        """
        using SlideRule;

        namespace ConfigExcludedSpecAssembly
        {
            public sealed class ConfigExcludedSpec : IArchitectureSpec
            {
                public void Define(Arch arch)
                {
                    arch.Rule("configexcluded/built-no-http")
                        .Enforce(arch.Project("ConfigExcluded.Built").MustNotReference(arch.Namespace("System.Net.Http.*")))
                        .Because("The ledger does no network calls.");
                }
            }
        }
        """;

    private static readonly Lazy<byte[]> SpecImage =
        new(() => SpecAssemblyCompiler.EmitImage(SpecSource, "ConfigExcludedSpec"));

    private static readonly (string Project, string Reason)[] ExpectedUnsupported =
    [
        ("ConfigExcluded.LegacySite", "a Web Site project, which has no project file"),
        ("ConfigExcluded.Shared/ConfigExcluded.Shared.csproj", "not built by the solution's default configuration"),
        ("ConfigExcluded.Skipped/ConfigExcluded.Skipped.csproj", "not built by the solution's default configuration"),
        ("ConfigExcluded.Portal", "a Web Site project, which has no project file")
    ];

    [Theory]
    [InlineData("graph")]
    [InlineData("check")]
    public async Task Json_RestoredSolution_NamesTheEntriesTheBuildSkipsWithoutRefusing(string verb)
    {
        // Arrange — the restore a reader would run, which follows the solution's configuration.
        using TempFixtureWorkspace workspace = new(Bed, Solution);
        File.Exists(workspace.PathOf("ConfigExcluded.Skipped", "obj", "project.assets.json"))
            .ShouldBeFalse("the solution restore skips the project its configuration does not build");

        // Act
        CliResult result = await Run(workspace, verb, "--json");

        // Assert — the run answers, and its coverage statement names every entry by what a reader can find in
        // the solution. No workspace diagnostic remains, so nothing reports the path the workspace
        // made up for either Web Site entry.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.UnsupportedProjectEntries()
            .ShouldBe(ExpectedUnsupported);
        IReadOnlyList<string> diagnostics = document.RootElement.TryGetProperty("workspaceDiagnostics", out _)
            ? document.RootElement.Strings("workspaceDiagnostics")
            : [];
        diagnostics.ShouldBeEmpty(result.Transcript());
        result.Transcript()
            .ShouldNotContain("localhost", customMessage: result.Transcript());
    }

    [Fact]
    public async Task Graph_RestoredSolution_SurveysOnlyTheProjectTheBuildBuilds()
    {
        // Arrange
        using TempFixtureWorkspace workspace = new(Bed, Solution);

        // Act
        CliResult result = await Run(workspace, "graph", "--json");

        // Assert — neither not-built project's types are in the model, as neither is in the build, the one the
        // built project references included.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.ProjectNames()
            .ShouldBe(["ConfigExcluded.Built"]);
    }

    [Fact]
    public async Task CheckJson_ReplayedBinlog_IsTakenAsACaptureAndNamesTheSameEntriesAsALoad()
    {
        // Arrange — a build of the solution, which compiles the built project alone. The not-built projects, the
        // referenced one included, and the Web Sites leave no compiler call in the binlog, so only the solution file
        // can name them, and a replay that read only the binlog would describe a smaller solution than a load of the
        // same tree.
        using var cache = new TempCacheRoot("solution-entries");
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(Bed, Solution, false);
        string binlog = BuildWithBinlog(workspace, "ConfigExcluded.binlog");
        string specDll = workspace.WriteSpec("ConfigExcludedSpec", SpecImage.Value);

        // Act — with the cache on, so the binlog is ingested as a capture as well, under a cache root no other
        // copy shares. The capture's coverage check passes a binlog that lacks the projects the build skips.
        CliResult result = await CliRunner.InvokeColdAsync(
            FakeEnvironment.WithCacheRoot(cache.Root),
            "check", workspace.SolutionPath, "--spec", specDll, "--binlog", binlog, "--json");

        // Assert
        result.ShouldSucceed();
        File.Exists(CaptureFiles.Manifest(workspace.SolutionPath, cache.Root))
            .ShouldBeTrue(result.Transcript());
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.UnsupportedProjectEntries()
            .ShouldBe(ExpectedUnsupported);
    }

    [Fact]
    public async Task CheckJson_NeverRestored_BlamesOnlyTheProjectTheBuildBuilds()
    {
        // Arrange — no restore at all, so no project has an assets file. The refusal's advice is a restore of the
        // solution, which restores the built project.
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(Bed, Solution, false);

        // Act
        CliResult result = await Run(workspace, "check", "--json");

        // Assert — only the project left in the model is named, and the advice restores it, so following the
        // advice ends the refusal.
        result.ShouldRefuseWith();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.Strings("restoreFailedProjects")
            .ShouldBe(["ConfigExcluded.Built/ConfigExcluded.Built.csproj"]);
    }

    [Fact]
    public async Task Check_NotBuiltSpecProject_RefusesNamingTheConfiguration()
    {
        // Arrange
        using TempFixtureWorkspace workspace = new(Bed, Solution);
        string specProject = workspace.PathOf("ConfigExcluded.Skipped", "ConfigExcluded.Skipped.csproj");

        // Act — the not-built project named as the spec, by the path a reader would type.
        CliResult result = await CliRunner.InvokeColdAsync(
            "check", workspace.SolutionPath, "--spec", specProject, "--no-cache");

        // Assert — the reason is the configuration, never a project missing from the solution.
        result.ShouldRefuseWithExactly(
            $"--spec '{specProject}' is not built by the solution's default configuration, so the "
            + "load leaves it out. Make that configuration build it, or pass its built DLL.");
    }

    [Fact]
    public async Task Check_NoSpecArgument_NamesTheProjectsTheBuildLeavesOutAsCandidates()
    {
        // Arrange
        using TempFixtureWorkspace workspace = new(Bed, Solution);

        // Act
        CliResult result = await CliRunner.InvokeColdAsync("check", workspace.SolutionPath, "--no-cache");

        // Assert — the clean-load sentence unchanged, then the projects the load left out.
        result.ShouldRefuseWithExactly(
            "No spec project found: no solution project references SlideRule.dll. Pass --spec to name one.\n"
            + "Considered 1 C# project(s) in the workspace.\n"
            + "The solution's default configuration does not build these projects, so the load left them out:\n"
            + "  ConfigExcluded.Shared/ConfigExcluded.Shared.csproj\n"
            + "  ConfigExcluded.Skipped/ConfigExcluded.Skipped.csproj\n"
            + "If the spec project is among them, make that configuration build it, or pass its built DLL to --spec.");
    }

    // Cold, with no cache: a pooled workspace or a cached model from another copy could answer for a tree the fact
    // did not stage.
    private static Task<CliResult> Run(TempFixtureWorkspace workspace, string verb, params string[] extra)
    {
        string[] spec = verb == "graph" ? [] : ["--spec", workspace.WriteSpec("ConfigExcludedSpec", SpecImage.Value)];
        string[] arguments = [verb, workspace.SolutionPath, .. spec, "--no-cache", .. extra];
        return CliRunner.InvokeColdAsync(arguments);
    }

    private static string BuildWithBinlog(TempFixtureWorkspace workspace, string binlogName, string? configuration = null)
    {
        string binlog = workspace.PathOf(binlogName);
        string configurationSwitch = configuration is null ? "" : $" -c {configuration}";
        DotnetCli.Run(
            $"build \"{workspace.SolutionPath}\"{configurationSwitch} -bl:LogFile=\"{binlog}\"",
            Path.GetDirectoryName(workspace.SolutionPath)!);
        return binlog;
    }
}
