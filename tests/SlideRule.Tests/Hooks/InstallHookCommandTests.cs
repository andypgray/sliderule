using Shouldly;
using SlideRule.Cli;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Cli;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     <c>sliderule install-hook</c> over repositories written under the run's temp root: where it writes, what a
///     second run does to the file, and the targets it refuses to write for.
/// </summary>
/// <remarks>
///     The runner is driven with the working directory as a request field, which the command line takes from the
///     process: a run through the command tree here would write into this repository's own settings.
/// </remarks>
public sealed class InstallHookCommandTests : IDisposable
{
    private const string SnippetPath = "hooks/settings.snippet.json";

    private static readonly DateTime Stamped = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    private readonly TempDirectory _temp = TestTempRoot.Fresh("install-hook");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void EmbeddedSnippet_IsTheCommittedFile()
    {
        // Act
        string embedded = EmbeddedResourceText.Load(InstallHookRunner.SnippetResource);

        // Assert
        embedded.ShouldBe(RepoRoot.ReadText(SnippetPath));
    }

    [Fact]
    public async Task Install_ThenAgain_WritesTheSnippetOnceAndLeavesTheFileAlone()
    {
        // Arrange
        string repository = Repository("repo");
        _temp.WriteFile(["repo", "App.slnx"], "<Solution />\n");
        string settings = _temp.PathOf("repo", ".claude", "settings.json");

        // Act
        CliResult first = await InstallAsync(repository);
        File.SetLastWriteTimeUtc(settings, Stamped);
        CliResult second = await InstallAsync(repository);

        // Assert
        first.ShouldSucceed();
        first.Out.NormalizedLines()
            .ShouldEndWith("added permissions.deny: PowerShell(*dnx SlideRule.Cli* baseline*)\nwrote .claude/settings.json\n");
        File.ReadAllText(settings)
            .ShouldBe(RepoRoot.ReadText(SnippetPath));
        second.ShouldSucceed();
        second.ShouldPrint(
            "kept UserPromptSubmit: sliderule hook\nkept Stop: sliderule hook\nkept SubagentStop: sliderule hook\n"
            + "nothing to change in .claude/settings.json");
        File.GetLastWriteTimeUtc(settings)
            .ShouldBe(Stamped);
    }

    [Fact]
    public async Task Install_FromBelowTheRoot_WritesAtTheRootWithTheSolutionRelativeToIt()
    {
        // Arrange
        Repository("below");
        _temp.WriteFile(["below", "src", "App.slnx"], "<Solution />\n");
        string source = _temp.PathOf("below", "src");

        // Act
        CliResult result = await InstallAsync(source, "App.slnx");

        // Assert
        result.ShouldSucceed("added Stop: sliderule hook src/App.slnx", "wrote ../.claude/settings.json");
        File.Exists(_temp.PathOf("below", ".claude", "settings.json"))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Install_ASolutionSpelledInAnotherCase_WritesTheNamesOnDisk()
    {
        // The committed entry is read by clones on case-sensitive file systems, which have only the names on disk.
        // Arrange
        Repository("cased");
        _temp.WriteFile(["cased", "src", "MyApp.slnx"], "<Solution />\n");
        string repository = _temp.PathOf("cased");
        Assert.SkipUnless(
            File.Exists(Path.Combine(repository, "SRC", "MYAPP.SLNX")),
            "The file system under the run's temp root is case-sensitive.");

        // Act
        CliResult result = await InstallAsync(repository, "SRC/MYAPP.SLNX");

        // Assert
        result.ShouldSucceed("added Stop: sliderule hook src/MyApp.slnx", "wrote .claude/settings.json");
    }

    [Fact]
    public async Task Install_ASolutionNamedByAPattern_RefusesWritingNothing()
    {
        // A literal '*' or '?' names no file; matching it against the directory would hand the hook another solution.
        // Arrange
        string repository = Repository("pattern");
        _temp.WriteFile(["pattern", "App.slnx"], "<Solution />\n");

        // Act
        CliResult result = await InstallAsync(repository, "*.slnx");

        // Assert
        result.ShouldRefuseWith(
            "Nothing was written to .claude/settings.json: ",
            "Pass the one it should check: sliderule install-hook <solution>.");
        Directory.Exists(Path.Combine(repository, ".claude"))
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    // A settings file beside a solution below the root governs runs from there down, not the hook's from the root.
    [InlineData(true)]
    public async Task Install_ASolutionTheHookWouldNotFindFromTheRoot_RefusesWritingNothing(bool settingsBeside)
    {
        // Arrange
        string repository = Repository(settingsBeside ? "beside" : "bare");
        _temp.WriteFile([Path.GetFileName(repository), "src", "App.slnx"], "<Solution />\n");
        if (settingsBeside)
            _temp.WriteFile([Path.GetFileName(repository), "src", "sliderule.json"], "{\"solution\": \"App.slnx\"}\n");

        // Act
        CliResult result = await InstallAsync(repository);

        // Assert
        result.ShouldRefuseWith(
            "Nothing was written to .claude/settings.json: ",
            "Pass the one it should check: sliderule install-hook <solution>.");
        Directory.Exists(Path.Combine(repository, ".claude"))
            .ShouldBeFalse();
    }

    private string Repository(string name)
    {
        string repository = _temp.PathOf(name);
        Directory.CreateDirectory(repository);
        GitCommand.Run(repository, "init");
        return repository;
    }

    private static Task<CliResult> InstallAsync(string workingDirectory, string? solution = null)
    {
        InstallHookRequest request = new(solution, null, workingDirectory, CommandTree.Root());
        // Under the handler the command line runs it under, which turns a refusal into exit 2 and its message.
        return CliResult.CapturedAsync((output, error) => CommandEntryPoint.RunAsync(
            () => new InstallHookRunner(output, new FakeEnvironment()).RunAsync(request, Ct), error, Ct));
    }
}
