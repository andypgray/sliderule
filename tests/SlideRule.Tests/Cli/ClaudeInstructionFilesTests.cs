using Shouldly;
using Xunit;
using SlideRule.Cli.Rendering;
using SlideRule.Rendering;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Cli;

/// <summary>
///     What <c>render</c> counts as a Claude Code instruction file standing between the client and the root
///     block: which text imports the block's file, which files the walk finds and in what order, and which it
///     never counts.
/// </summary>
/// <remarks>
///     The import rows need no disk, because the rule is a function of the text, the file's directory and the
///     block file's path. The walk rows run on a fresh temp tree and read only what the walk found inside it:
///     the walk climbs on past the tree to the filesystem root, and what sits up there is the machine's.
/// </remarks>
public sealed class ClaudeInstructionFilesTests
{
    // A repository with its solution in src, spelled for the host. Nothing is ever written here.
    private static readonly string Repository = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "claude-files", "repo"));

    private static readonly string BlockFile = Path.Combine(Repository, "src", "AGENTS.md");

    private static readonly IReadOnlySet<string> NothingExcluded = new HashSet<string>();

    [Theory]
    [InlineData("@AGENTS.md\n", "src")] // bare, beside the block
    [InlineData("@./AGENTS.md\n", "src")]
    [InlineData("@../AGENTS.md\n", "src/.claude")] // .claude/CLAUDE.md reaching back up
    [InlineData("@src/AGENTS.md\n", ".")] // a CLAUDE.md at the repository root
    [InlineData("# Rules\n\nRead the block first: @AGENTS.md\n", "src")] // after whitespace, mid-line
    [InlineData("```\n@AGENTS.md\n```\n\n@AGENTS.md\n", "src")] // after a fence has closed
    [InlineData("A stray ` stays literal, and @AGENTS.md still counts.\n", "src")] // a backtick with no partner
    [InlineData("``a ` b`` then @AGENTS.md\n", "src")] // after a span that holds a shorter backtick run
    public void Imports_AnImportOfTheBlockFile_Counts(string text, string fileDirectory)
    {
        ClaudeInstructionFiles.Imports(text, Under(fileDirectory), BlockFile, null)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData("```\n@AGENTS.md\n```\n")] // inside a fenced code block
    [InlineData("~~~markdown\n@AGENTS.md\n~~~\n")]
    [InlineData("Put `@AGENTS.md` in your own CLAUDE.md.\n")] // inside a code span
    [InlineData("`` `@AGENTS.md` ``\n")] // inside a double-backtick span
    [InlineData("x@AGENTS.md\n")] // glued to the word before it
    [InlineData("See @AGENTS.md.\n")] // the full stop is part of the path
    [InlineData("<!-- @AGENTS.md -->\n")] // inside an HTML comment
    [InlineData("<!--\n@AGENTS.md\n-->\n")] // inside a comment across lines
    [InlineData("@README.md\n")] // another file
    [InlineData("@docs/AGENTS.md\n")] // another directory's card
    public void Imports_AnythingElse_DoesNotCount(string text)
    {
        ClaudeInstructionFiles.Imports(text, Under("src"), BlockFile, null)
            .ShouldBeFalse();
    }

    [Fact]
    public void Imports_AHomeRelativeImport_ResolvesAgainstTheHomeDirectoryAlone()
    {
        const string text = "@~/repo/src/AGENTS.md\n";
        string home = Path.GetDirectoryName(Repository)!;

        ClaudeInstructionFiles.Imports(text, Under("."), BlockFile, home)
            .ShouldBeTrue();
        ClaudeInstructionFiles.Imports(text, Under("."), BlockFile, null)
            .ShouldBeFalse();
    }

    [Fact]
    public void Find_WalksUpFromTheSolution_NearestFirst_InTheClientsOrderWithinADirectory()
    {
        using TempDirectory tree = TestTempRoot.Fresh("claude-files");
        string parent = tree.WriteFile(["repo", "CLAUDE.md"], "parent");
        string local = tree.WriteFile(["repo", "src", "CLAUDE.local.md"], "local");
        string dotClaude = tree.WriteFile(["repo", "src", ".claude", "CLAUDE.md"], "dot");
        string plain = tree.WriteFile(["repo", "src", "CLAUDE.md"], "plain");

        FoundInside(tree, tree.PathOf("repo", "src"), NothingExcluded)
            .ShouldBe([plain, dotClaude, local, parent]);
    }

    [Fact]
    public void Find_TheUserMemoryFile_IsNeverCounted()
    {
        using TempDirectory tree = TestTempRoot.Fresh("claude-files");
        string memory = tree.WriteFile(["home", ".claude", "CLAUDE.md"], "user memory");
        string solution = tree.PathOf("home", "repo");
        Directory.CreateDirectory(solution);
        IReadOnlySet<string> excluded = ClaudeInstructionFiles.UserMemoryFiles(HomeAt(tree.PathOf("home")));

        // Found while nothing is excluded, so the exclusion is what removes it on the second walk.
        FoundInside(tree, solution, NothingExcluded)
            .ShouldBe([memory]);
        FoundInside(tree, solution, excluded)
            .ShouldBeEmpty();
    }

    [Fact]
    public void UserMemoryFiles_AreTheHomeFileAndTheConfigDirectoryFile()
    {
        string home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "claude-home"));
        string config = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "claude-config"));
        FakeEnvironment environment = HomeAt(home)
            .SetVariable(SlideRuleEnvVars.ClaudeConfigDirectory, config);

        ClaudeInstructionFiles.UserMemoryFiles(environment)
            .ShouldBe([Path.Combine(home, ".claude", "CLAUDE.md"), Path.Combine(config, "CLAUDE.md")], ignoreOrder: true);
    }

    [Fact]
    public void ImportsBlockFile_ALinkToTheBlockFile_Counts()
    {
        using TempDirectory tree = TestTempRoot.Fresh("claude-files");
        string block = tree.WriteFile(["repo", "AGENTS.md"], "<!-- sliderule:begin -->\n<!-- sliderule:end -->\n");
        string link = tree.PathOf("repo", "CLAUDE.md");
        SymlinkSupport.CreateFileSymlink(link, block);

        // The link reads as the block itself, which holds no import line, so the link is the whole answer.
        ClaudeInstructionFiles.ImportsBlockFile(link, block, null)
            .ShouldBeTrue();
    }

    [Fact]
    public void ImportsBlockFile_AFileThatCannotBeRead_CountsAsNotImporting()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses a read while another handle holds the file exclusively.");
        using TempDirectory tree = TestTempRoot.Fresh("claude-files");
        string block = tree.WriteFile(["repo", "AGENTS.md"], "block");
        string file = tree.WriteFile(["repo", "CLAUDE.md"], "@AGENTS.md\n");
        using var holder = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);

        // The text imports the block, so only the refused read can make the answer false.
        ClaudeInstructionFiles.ImportsBlockFile(file, block, null)
            .ShouldBeFalse();
    }

    private static string Under(string relative)
    {
        return Path.GetFullPath(Path.Combine(Repository, relative));
    }

    // Home as the client reads it on either host, so one fake serves Windows and every other platform alike.
    private static FakeEnvironment HomeAt(string home)
    {
        return new FakeEnvironment().SetVariable(SlideRuleEnvVars.UserProfile, home)
            .SetVariable(SlideRuleEnvVars.Home, home);
    }

    private static List<string> FoundInside(TempDirectory tree, string solution, IReadOnlySet<string> excluded)
    {
        return ClaudeInstructionFiles.Find(solution, excluded)
            .Where(path => PathFormat.Contains(tree.Path, path))
            .ToList();
    }
}
