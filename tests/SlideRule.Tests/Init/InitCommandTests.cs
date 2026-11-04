using Shouldly;
using SlideRule.Cli.Init;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Init;

/// <summary>
///     <c>sliderule init</c> through the command tree, over product trees written under the run's temp root, which
///     no git work tree encloses, so <c>init</c> writes its settings beside the solution unless a row initialises a
///     repository of its own.
/// </summary>
public sealed class InitCommandTests : IDisposable
{
    private readonly TempDirectory _temp = TestTempRoot.Fresh("init-command");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public async Task Init_FromTheGraph_ScaffoldsAddsBuildsAndPassesTheRecordedRules()
    {
        // Arrange
        string solution = RestoredProductTree("graph", withReports: true);
        ScriptedSpecProjectCommands commands = ScriptedSpecProjectCommands.Real();

        // Act
        CliResult result = await CliRunner.InvokeWithSpecProjectCommandsAsync(commands, "init", solution, "--from-graph");

        // Assert
        result.ShouldSucceed(
            "pass layering/myapp-independent — The MyApp layer must not reference the MyApp.Reports layer.",
            "pass layering/myapp-reports-independent — The MyApp.Reports layer must not reference the MyApp layer.");
        string project = _temp.PathOf("graph", "arch", "MyApp.Architecture", "MyApp.Architecture.csproj");
        commands.Calls.ShouldBe([$"add {solution} {project}", $"build {project}"]);
        File.ReadAllText(_temp.PathOf("graph", "sliderule.json")).ShouldBe(
            "{\n  \"solution\": \"MyApp.slnx\",\n  \"spec\": \"arch/MyApp.Architecture/MyApp.Architecture.csproj\"\n}\n");
    }

    [Fact]
    public async Task Init_WritesTheRecipesScaffoldAndSettingsBeforeItBuilds()
    {
        // Both commands are answered without running, so the check that follows finds the spec project in no
        // solution: what this row reads is every file init wrote on its way there, and the order it reported them in.
        // Arrange
        string solution = RestoredProductTree("plain");
        ScriptedSpecProjectCommands commands = ScriptedSpecProjectCommands.Answering();

        // Act
        CliResult result = await CliRunner.InvokeWithSpecProjectCommandsAsync(commands, "init", solution);

        // Assert
        result.ShouldRefuseWith("MyApp.Architecture.csproj' is not a project in the solution.");
        result.Out.ShouldStartWith(string.Join(
            "\n",
            "wrote arch/MyApp.Architecture/MyApp.Architecture.csproj",
            "wrote arch/MyApp.Architecture/ArchitectureSpec.cs",
            "added arch/MyApp.Architecture/MyApp.Architecture.csproj to MyApp.slnx",
            "wrote sliderule.json",
            "built arch/MyApp.Architecture/MyApp.Architecture.csproj").ReplaceLineEndings());
        File.ReadAllText(_temp.PathOf("plain", "arch", "MyApp.Architecture", "MyApp.Architecture.csproj"))
            .ShouldBe(SpecScaffold.Embedded.Project(ServerVersion.SemVer, "net10.0"));
        File.ReadAllText(_temp.PathOf("plain", "arch", "MyApp.Architecture", "ArchitectureSpec.cs"))
            .ShouldBe(SpecScaffold.Embedded.SpecClass("MyApp.Architecture", null, []));
    }

    [Fact]
    public async Task Init_AFailedSolutionAdd_ExitsTwoWithTheCommandsThatFinishByHand()
    {
        // Arrange
        string solution = RestoredProductTree("failed-add");
        ScriptedSpecProjectCommands commands = ScriptedSpecProjectCommands.Answering(add: new ChildProcess.ProcessResult(1, "add said no", ""));

        // Act
        CliResult result = await CliRunner.InvokeWithSpecProjectCommandsAsync(commands, "init", solution);

        // Assert
        string project = _temp.PathOf("failed-add", "arch", "MyApp.Architecture", "MyApp.Architecture.csproj");
        commands.Calls.ShouldBe([$"add {solution} {project}"], "the build should not run after a failed solution add");
        File.Exists(_temp.PathOf("failed-add", "sliderule.json")).ShouldBeFalse();
        result.ShouldRefuseWith(
            "add said no",
            "dotnet exited 1.",
            "init wrote arch/MyApp.Architecture/MyApp.Architecture.csproj and arch/MyApp.Architecture/ArchitectureSpec.cs",
            "  dotnet sln ",
            "\"spec\": \"arch/MyApp.Architecture/MyApp.Architecture.csproj\"",
            "  dotnet build ",
            "  sliderule check");
    }

    [Fact]
    public async Task Init_AFailedBuild_ExitsTwoWithTheBuildAndTheCheckLeftToRun()
    {
        // Arrange
        string solution = RestoredProductTree("failed-build");
        ScriptedSpecProjectCommands commands = ScriptedSpecProjectCommands.Answering(build: new ChildProcess.ProcessResult(1, "", "CS1002"));

        // Act
        CliResult result = await CliRunner.InvokeWithSpecProjectCommandsAsync(commands, "init", solution);

        // Assert
        result.ShouldRefuseWith("CS1002", "dotnet exited 1.", "  dotnet build ", "  sliderule check");
        result.Err.ShouldNotContain("dotnet sln ");
        File.Exists(_temp.PathOf("failed-build", "sliderule.json")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("app")]
    // git prints the path below the root as written, so a name's own leading space is part of it.
    [InlineData(" app")]
    public async Task Init_InsideAGitRepository_WritesTheSettingsAtItsRoot(string folder)
    {
        // Arrange
        string solution = RestoredProductTree(Path.Combine("repo", folder));
        GitCommand.Run(_temp.PathOf("repo"), "init");
        ScriptedSpecProjectCommands commands = ScriptedSpecProjectCommands.Answering();

        // Act
        await CliRunner.InvokeWithSpecProjectCommandsAsync(commands, "init", solution);

        // Assert
        ShouldHaveWrittenSettingsAtRoot("repo", folder);
    }

    [Fact]
    public async Task Init_ThroughAShortNameBelowTheRepositoryRoot_WritesTheSettingsAtTheRootInLongNames()
    {
        // A solution reached through a folder's short name still has its root found, and the committed settings record
        // the long names another clone will have.
        // Arrange
        string longSolution = ProductTree(Path.Combine("short", "Application Folder"));
        string shortFolder = ShortPaths.ShortSpellingOrSkip(_temp.PathOf("short", "Application Folder"));
        Restore(longSolution);
        GitCommand.Run(_temp.PathOf("short"), "init");
        string solution = Path.Join(shortFolder, Path.GetFileName(longSolution));
        ScriptedSpecProjectCommands commands = ScriptedSpecProjectCommands.Answering();

        // Act
        await CliRunner.InvokeWithSpecProjectCommandsAsync(commands, "init", solution);

        // Assert
        ShouldHaveWrittenSettingsAtRoot("short", "Application Folder");
    }

    [Fact]
    public async Task Init_ASettingsFileAtTheRepositoryRoot_RefusesBeforeWritingAnything()
    {
        string solution = ProductTree(Path.Combine("rooted", "app"));
        GitCommand.Run(_temp.PathOf("rooted"), "init");
        _temp.WriteFile(["rooted", "sliderule.json"], """{ "solution": "app/MyApp.slnx" }""");

        await ShouldRefuseUntouchedAsync("rooted", solution, "sliderule.json already exists");
    }

    [Fact]
    public async Task Init_AStrayGitDirectoryThatGitDoesNotRecognise_IsNoRepositoryRoot()
    {
        // An empty .git directory is no repository to git, so the settings go beside the solution as they do
        // outside any work tree; a walk that stopped at the first .git would write them one level up.
        // Arrange
        string solution = RestoredProductTree(Path.Combine("stray", "app"));
        Directory.CreateDirectory(_temp.PathOf("stray", ".git"));
        ScriptedSpecProjectCommands commands = ScriptedSpecProjectCommands.Answering();

        // Act
        await CliRunner.InvokeWithSpecProjectCommandsAsync(commands, "init", solution);

        // Assert
        File.Exists(_temp.PathOf("stray", "app", "sliderule.json"))
            .ShouldBeTrue("git names no work tree here, so the settings go beside the solution");
        File.Exists(_temp.PathOf("stray", "sliderule.json"))
            .ShouldBeFalse("a directory git does not recognise as a repository is not a repository root");
    }

    [Fact]
    public async Task Init_ASettingsFileBesideTheSolution_RefusesBeforeWritingAnything()
    {
        string solution = ProductTree("settings");
        _temp.WriteFile(["settings", "sliderule.json"], """{ "solution": "MyApp.slnx" }""");

        await ShouldRefuseUntouchedAsync("settings", solution, "sliderule.json already exists");
    }

    [Fact]
    public async Task Init_AnExistingSpecFolder_RefusesBeforeWritingAnything()
    {
        string solution = ProductTree("folder");
        Directory.CreateDirectory(_temp.PathOf("folder", "arch", "MyApp.Architecture"));

        await ShouldRefuseUntouchedAsync("folder", solution, "MyApp.Architecture already exists");
    }

    [Fact]
    public async Task Init_AProjectBesideTheSolution_RefusesBecauseItWouldCompileTheSpec()
    {
        string solution = ProductTree("enclosing");
        _temp.WriteFile(["enclosing", "Root.csproj"], "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        await ShouldRefuseUntouchedAsync(
            "enclosing", solution, "Root.csproj would compile the spec class", "Pass --output");
    }

    [Fact]
    public async Task Init_ASolutionFilter_RefusesBeforeWritingAnything()
    {
        ProductTree("filter");
        string filter = _temp.WriteFile(["filter", "MyApp.slnf"], """{ "solution": { "path": "MyApp.slnx", "projects": [] } }""");

        await ShouldRefuseUntouchedAsync("filter", filter, "is a solution filter");
    }

    [Fact]
    public async Task Init_ASolutionWithASpecProject_RefusesNamingIt()
    {
        // A project named SlideRule stands in for a source checkout's contract project, so nothing needs a feed.
        // Arrange
        string solution = ProductTree("existing-spec");
        _temp.WriteFile(["existing-spec", "src", "SlideRule", "SlideRule.csproj"], ScratchProjects.ClassLibrary());
        _temp.WriteFile(["existing-spec", "src", "SlideRule", "Arch.cs"], "namespace SlideRule;\npublic sealed class Arch { }\n");
        _temp.WriteFile(
            ["existing-spec", "src", "MyApp.Rules", "MyApp.Rules.csproj"],
            ScratchProjects.ClassLibrary("<ItemGroup><ProjectReference Include=\"../SlideRule/SlideRule.csproj\" /></ItemGroup>"));
        _temp.WriteFile(["existing-spec", "src", "MyApp.Rules", "Rules.cs"], "namespace MyApp.Rules;\npublic sealed class Rules { }\n");
        _temp.WriteFile(["existing-spec", "MyApp.slnx"], ScratchProjects.Solution("src/MyApp/MyApp.csproj", "src/MyApp.Rules/MyApp.Rules.csproj"));
        Restore(solution);

        // Act and assert
        await ShouldRefuseUntouchedAsync(
            "existing-spec", solution, "The solution already has a spec project, MyApp.Rules, which references SlideRule.");
    }

    // Refused with every fragment on stderr, no command run, and neither the spec folder nor a settings file
    // written where none stood before.
    private async Task ShouldRefuseUntouchedAsync(string tree, string solution, params string[] fragments)
    {
        string root = _temp.PathOf(tree);
        string[] beforeFiles = WriteFootprint.Of(root);
        string[] beforeDirectories = Directories();
        ScriptedSpecProjectCommands commands = ScriptedSpecProjectCommands.Answering();

        CliResult result = await CliRunner.InvokeWithSpecProjectCommandsAsync(commands, "init", solution);

        result.ShouldRefuseWith(fragments);
        commands.Calls.ShouldBeEmpty("a refused init should run no dotnet command");
        WriteFootprint.Of(root).ShouldBe(beforeFiles, "a refused init should write nothing");
        Directories().ShouldBe(beforeDirectories, "a refused init should create no directory");

        string[] Directories()
        {
            return Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Select(directory => Path.GetRelativePath(root, directory))
                .Where(directory => !TempFixtureWorkspace.IsBuildArtifact(directory + "/"))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
    }

    // The settings init writes at root for a solution in folder below it, and none beside the solution.
    private void ShouldHaveWrittenSettingsAtRoot(string root, string folder)
    {
        File.ReadAllText(_temp.PathOf(root, "sliderule.json")).ShouldBe(
            $"{{\n  \"solution\": \"{folder}/MyApp.slnx\",\n  \"spec\": \"{folder}/arch/MyApp.Architecture/MyApp.Architecture.csproj\"\n}}\n");
        File.Exists(_temp.PathOf(root, folder, "sliderule.json"))
            .ShouldBeFalse("the settings belong at the repository root, not beside the solution");
    }

    private static void Restore(string solution)
    {
        DotnetCli.Run($"restore \"{solution}\"", Path.GetDirectoryName(solution)!);
    }

    private string RestoredProductTree(string tree, bool withReports = false)
    {
        string solution = ProductTree(tree, withReports);
        Restore(solution);
        return solution;
    }

    // MyApp.slnx over src/MyApp, and src/MyApp.Reports beside it, unconnected, where withReports asks for it.
    private string ProductTree(string tree, bool withReports = false)
    {
        _temp.WriteFile([tree, "src", "MyApp", "MyApp.csproj"], ScratchProjects.ClassLibrary());
        _temp.WriteFile([tree, "src", "MyApp", "Shipments.cs"], "namespace MyApp;\npublic interface IShipmentPort { }\n");
        if (!withReports) return _temp.WriteFile([tree, "MyApp.slnx"], ScratchProjects.Solution("src/MyApp/MyApp.csproj"));

        _temp.WriteFile([tree, "src", "MyApp.Reports", "MyApp.Reports.csproj"], ScratchProjects.ClassLibrary());
        _temp.WriteFile([tree, "src", "MyApp.Reports", "Report.cs"], "namespace MyApp.Reports;\npublic sealed class Report { }\n");
        return _temp.WriteFile(
            [tree, "MyApp.slnx"], ScratchProjects.Solution("src/MyApp/MyApp.csproj", "src/MyApp.Reports/MyApp.Reports.csproj"));
    }
}
