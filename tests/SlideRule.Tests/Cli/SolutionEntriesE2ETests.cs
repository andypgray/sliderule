using System.Text.Json;
using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.Replay;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end <c>graph</c> and <c>check</c> over a solution declaring entries its default configuration does
///     not build, and over a second solution declaring a shared project and a database project beside one C#
///     project. The first solution maps two C# projects with no <c>Build.0</c> line in its default <c>Debug</c>
///     configuration and builds them in <c>Release</c>, and its built project references one of the two. The first
///     solution also declares two Web Site entries, one with a URL path and one with a folder path.
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
///     <para>
///         A replay describes what its binlog compiled. A <c>Release</c> binlog compiles both projects the default
///         configuration skips, so its model holds them as the solution's own and lists only the entries no build
///         compiles.
///     </para>
///     <para>
///         A shared project and a database project leave no compiler call in a binlog either, so a replay names
///         them from the solution file, as a load does.
///     </para>
///     <para>
///         A project in another language that a C# project references and the solution does not declare reaches
///         the load only as the workspace's failure to open it. A scratch bed pins that the load names it from that
///         failure, and that the failure does not read as a load the spec project may have been lost in. A
///         declared one absent from the tree is listed the same way.
///     </para>
/// </remarks>
public sealed class SolutionEntriesE2ETests
{
    private const string Bed = "RestoreFailureSolutions/ConfigExcluded";

    private const string Solution = "ConfigExcluded.sln";

    private const string UnopenedBed = "RestoreFailureSolutions/UnopenedEntries";

    private const string UnopenedSolution = "UnopenedEntries.sln";

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

    // One rule into the VB project's layer, the direction a C# compilation can see, and one out of it, whose
    // subject no run surveys.
    private const string VisualBasicSpecSource =
        """
        using SlideRule;

        namespace VisualBasicSpecAssembly
        {
            public sealed class VisualBasicSpec : IArchitectureSpec
            {
                public void Define(Arch arch)
                {
                    Layer app = arch.Layer("App", arch.Project("App"));
                    Layer legacy = arch.Layer("Legacy", arch.Project("Legacy"));
                    arch.Rule("vb/app-off-legacy")
                        .Enforce(app.MustNotReference(legacy))
                        .Because("App does not reach the VB ledger.");
                    arch.Rule("vb/legacy-off-app")
                        .Enforce(legacy.MustNotReference(app))
                        .Because("The VB ledger does not reach App.");
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

    // What no configuration compiles, so what a Release binlog's replay still lists.
    private static readonly (string Project, string Reason)[] ExpectedUnsupportedInRelease =
    [
        ("ConfigExcluded.LegacySite", "a Web Site project, which has no project file"),
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
    public async Task Json_ReplayedReleaseBinlog_KeepsTheProjectsItCompiledAsMembers()
    {
        // Arrange — a Release build, which compiles both projects the default configuration skips.
        using var cache = new TempCacheRoot("solution-entries-release");
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(Bed, Solution, false);
        string binlog = BuildWithBinlog(workspace, "ConfigExcluded.Release.binlog", "Release");
        string specDll = workspace.WriteSpec("ConfigExcludedSpec", SpecImage.Value);
        FakeEnvironment environment = FakeEnvironment.WithCacheRoot(cache.Root);

        // Act — the check ingests the binlog as a capture; the survey reads the model it replays.
        CliResult check = await CliRunner.InvokeColdAsync(
            environment, "check", workspace.SolutionPath, "--spec", specDll, "--binlog", binlog, "--json");
        CliResult graph = await CliRunner.InvokeColdAsync(
            environment, "graph", workspace.SolutionPath, "--binlog", binlog, "--json");

        // Assert — the capture's coverage check accepts the projects the binlog compiled beyond the default
        // configuration, the model holds them as the solution's own, and neither is listed.
        check.ShouldSucceed();
        File.Exists(CaptureFiles.Manifest(workspace.SolutionPath, cache.Root))
            .ShouldBeTrue(check.Transcript());
        using JsonDocument checkDocument = check.ShouldHaveJsonStdout();
        checkDocument.UnsupportedProjectEntries()
            .ShouldBe(ExpectedUnsupportedInRelease);
        graph.ShouldSucceed();
        using JsonDocument graphDocument = graph.ShouldHaveJsonStdout();
        graphDocument.ProjectMemberships()
            .ShouldBe([("ConfigExcluded.Built", true), ("ConfigExcluded.Shared", true), ("ConfigExcluded.Skipped", true)]);
        graphDocument.UnsupportedProjectEntries()
            .ShouldBe(ExpectedUnsupportedInRelease);
    }

    [Fact]
    public async Task GraphJson_ReplayedBinlog_NamesTheSharedAndDatabaseProjectsALoadNames()
    {
        // Arrange — a build of the second bed, which compiles its one C# project.
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(UnopenedBed, UnopenedSolution);
        string binlog = BuildWithBinlog(workspace, "UnopenedEntries.binlog");
        (string Project, string Reason)[] expected =
        [
            ("UnopenedEntries.Common/UnopenedEntries.Common.shproj", "a shared project, compiled into the projects that import it"),
            ("UnopenedEntries.Database/UnopenedEntries.Database.sqlproj", "not a C# project")
        ];

        // Act
        CliResult load = await CliRunner.InvokeColdAsync("graph", workspace.SolutionPath, "--no-cache", "--json");
        CliResult replay = await CliRunner.InvokeColdAsync(
            "graph", workspace.SolutionPath, "--binlog", binlog, "--no-cache", "--json");

        // Assert
        load.ShouldSucceed();
        using JsonDocument loadDocument = load.ShouldHaveJsonStdout();
        loadDocument.UnsupportedProjectEntries()
            .ShouldBe(expected);
        loadDocument.RootElement.Strings("workspaceDiagnostics")
            .ShouldContain(
                diagnostic => diagnostic.Contains("UnopenedEntries.Common.shproj", StringComparison.Ordinal),
                "the load document keeps the workspace's line for the shared project:\n" + load.Transcript());
        replay.ShouldSucceed();
        using JsonDocument replayDocument = replay.ShouldHaveJsonStdout();
        replayDocument.UnsupportedProjectEntries()
            .ShouldBe(expected);
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

    [Fact]
    public async Task Check_NoSpecArgument_EntriesNoWorkspaceOpens_GetsTheCleanLoadRefusal()
    {
        // Arrange — restored, so the workspace's failures for the shared and database projects are the only lines
        // the load reports.
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(UnopenedBed, UnopenedSolution);

        // Act
        CliResult result = await CliRunner.InvokeColdAsync("check", workspace.SolutionPath, "--no-cache");

        // Assert — neither entry can be the spec project and no restore opens one, so the refusal sends the reader
        // to --spec rather than to a restore.
        result.ShouldRefuseWithExactly(
            "No spec project found: no solution project references SlideRule.dll. Pass --spec to name one.\n"
            + "Considered 1 C# project(s) in the workspace.");
    }

    [Fact]
    public async Task Json_ReferencedProjectsInAnotherLanguage_AreListedOnALoadAndAReplayAndGetTheCleanLoadRefusal()
    {
        // Arrange — one C# project referencing a C++, a database and a JavaScript project that the solution does not
        // declare. The workspace drops each reference and reports the project as one it opens in no language; a
        // replay reads each off the build's evaluation of the C# project. The solution also declares a generated
        // C++ project that is not on disk, as a premake-built solution does before generation. Each referenced
        // project carries the targets a build of its referrer calls, and builds nothing.
        using TempDirectory bed = TestTempRoot.Fresh("referenced-other-language");
        bed.WriteFile(
            ["App", "App.csproj"],
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Native\Native.vcxproj" />
                <ProjectReference Include="..\Db\Db.sqlproj" />
                <ProjectReference Include="..\Web\Web.esproj" />
              </ItemGroup>
            </Project>
            """);
        bed.WriteFile(["App", "Widget.cs"], "namespace App;\n\npublic sealed class Widget;\n");
        foreach (string[] project in (string[][])[["Native", "Native.vcxproj"], ["Db", "Db.sqlproj"], ["Web", "Web.esproj"]])
            bed.WriteFile(project, ReferencedStub);
        string solution = bed.WriteFile(["App.slnx"], ScratchProjects.Solution("App/App.csproj", "Gen/Gen.vcxproj"));
        string binlog = bed.PathOf("App.binlog");
        DotnetCli.Run($"build App/App.csproj -bl:LogFile=\"{binlog}\"", bed.Path);
        (string Project, string Reason)[] expected =
        [
            ("Db/Db.sqlproj", "not a C# project"),
            ("Gen/Gen.vcxproj", "not a C# project"),
            ("Native/Native.vcxproj", "not a C# project"),
            ("Web/Web.esproj", "not a C# project")
        ];

        // Act
        CliResult graph = await CliRunner.InvokeColdAsync("graph", solution, "--no-cache", "--json");
        CliResult replay = await CliRunner.InvokeColdAsync("graph", solution, "--binlog", binlog, "--no-cache", "--json");
        CliResult check = await CliRunner.InvokeColdAsync("check", solution, "--no-cache");

        // Assert — each is named as a project outside the survey, and none can be the spec project, so the refusal
        // sends the reader to --spec rather than to a restore that cannot open them.
        graph.ShouldSucceed();
        using JsonDocument document = graph.ShouldHaveJsonStdout();
        document.UnsupportedProjectEntries()
            .ShouldBe(expected);
        replay.ShouldSucceed();
        using JsonDocument replayDocument = replay.ShouldHaveJsonStdout();
        replayDocument.UnsupportedProjectEntries()
            .ShouldBe(expected);
        check.ShouldRefuseWithExactly(
            "No spec project found: no solution project references SlideRule.dll. Pass --spec to name one.\n"
            + "Considered 1 C# project(s) in the workspace.");
    }

    [Fact]
    public async Task CheckJson_VisualBasicProject_IsListedAndARuleOnItGetsTheCure()
    {
        // Arrange
        using TempDirectory bed = TestTempRoot.Fresh("visual-basic-member");
        (string solution, string spec) = WriteVisualBasicBed(bed);

        // Act
        CliResult result = await CliRunner.InvokeColdAsync("check", solution, "--spec", spec, "--no-cache", "--json");

        // Assert — the VB project is named as outside the survey, and a rule whose subject is the VB layer fails
        // empty, and its cure says why, rather than sending the reader after a misspelt project name that is spelt
        // right.
        result.ShouldReportViolations();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.UnsupportedProjectEntries()
            .ShouldBe([("Legacy/Legacy.vbproj", "not a C# project")]);
        result.Out.ShouldHaveFailed("vb/legacy-off-app");
        JsonElement emptySubject = CheckJson.Rule(document, "vb/legacy-off-app")
            .GetProperty("violations")
            .EnumerateArray()
            .ShouldHaveSingleItem(result.Transcript());
        emptySubject.GetProperty("kind")
            .GetString()
            .ShouldBe("emptySubject", result.Transcript());
        emptySubject.GetProperty("hint")
            .GetString()
            .ShouldBe(LegacyCure, result.Transcript());

        // The text channel prints the same cure as the rule's hint line.
        CliResult human = await CliRunner.InvokeColdAsync("check", solution, "--spec", spec, "--no-cache");
        human.ShouldReportViolations("hint: " + LegacyCure);
    }

    [Fact]
    public async Task CheckJson_ReferenceFromCSharpIntoAVisualBasicProject_IsChecked()
    {
        // Arrange
        using TempDirectory bed = TestTempRoot.Fresh("visual-basic-reference");
        (string solution, string spec) = WriteVisualBasicBed(bed);

        // Act
        CliResult result = await CliRunner.InvokeColdAsync("check", solution, "--spec", spec, "--no-cache", "--json");

        // Assert — a reference into the VB project from C# is still checked, on every platform, because the C#
        // compilation reaches its types through the project's assembly, and the load says nothing about it.
        result.Out.ShouldHaveSingleViolationOnEdge("vb/app-off-legacy", "reference", "App.Widget", "Legacy.Ledger");
        result.Err.ShouldNotContain(CrossLanguageReferences.Message(LegacyReference(bed)), customMessage: result.Transcript());
    }

    [Fact]
    public async Task Check_ReferenceIntoAVisualBasicProjectWithDeclarationErrors_WarnsThatItsTypesAreUnbound()
    {
        // Arrange — the bed builds, and then the VB project gains a declaration error, so the workspace has no
        // assembly to hand the C# compilation.
        using TempDirectory bed = TestTempRoot.Fresh("visual-basic-broken");
        (string solution, string spec) = WriteVisualBasicBed(bed);
        bed.WriteFile(
            ["Legacy", "Ledger.vb"],
            "Namespace Legacy\n    Public Class Ledger\n        Inherits Missing\n    End Class\nEnd Namespace\n");

        // Act
        CliResult result = await CliRunner.InvokeColdAsync("check", solution, "--spec", spec, "--no-cache");

        // Assert
        result.Err.ShouldContain(
            "warning: " + CrossLanguageReferences.Message(LegacyReference(bed)), customMessage: result.Transcript());
    }

    // The bed's one cross-language reference, as the load names it.
    private static CrossLanguageReferences.Finding LegacyReference(TempDirectory bed)
    {
        return new CrossLanguageReferences.Finding(bed.PathOf("App", "App.csproj"), bed.PathOf("Legacy", "Legacy.vbproj"));
    }

    // A C# project referencing an SDK-style Visual Basic one, both declared and built, and the spec that layers them.
    // The workspace loads the VB project without a failure, and extraction skips it by language.
    private static (string Solution, string Spec) WriteVisualBasicBed(TempDirectory bed)
    {
        bed.WriteFile(
            ["App", "App.csproj"],
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Legacy\Legacy.vbproj" />
              </ItemGroup>
            </Project>
            """);
        bed.WriteFile(["App", "Widget.cs"], "namespace App;\n\npublic sealed class Widget\n{\n    public Legacy.Ledger? Ledger { get; }\n}\n");
        bed.WriteFile(
            ["Legacy", "Legacy.vbproj"],
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        bed.WriteFile(["Legacy", "Ledger.vb"], "Namespace Legacy\n    Public Class Ledger\n    End Class\nEnd Namespace\n");
        string solution = bed.WriteFile(["App.slnx"], ScratchProjects.Solution("App/App.csproj", "Legacy/Legacy.vbproj"));
        DotnetCli.Run("build App.slnx", bed.Path);
        string spec = SpecAssemblyCompiler.EmitSpecDllToFreshTemp("visual-basic-member", "VisualBasic", VisualBasicSpecSource);
        return (solution, spec);
    }

    // A project in another language that a C# project references: the targets a build of the referrer calls on a
    // reference, each doing nothing, so the referrer builds and the build writes a binary log.
    private const string ReferencedStub =
        """
        <Project>
          <Target Name="Build" />
          <Target Name="GetTargetFrameworks" />
          <Target Name="GetTargetPath" />
          <Target Name="GetNativeManifest" />
          <Target Name="GetCopyToOutputDirectoryItems" />
          <Target Name="GetCopyToPublishDirectoryItems" />
        </Project>
        """;

    // The cure a rule anchored on the bed's VB project reports.
    private const string LegacyCure =
        "Legacy/Legacy.vbproj is a project this run does not survey (not a C# project), so a selection anchored on "
        + "it matches nothing; anchor the rule on C# projects, or remove it.";

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
