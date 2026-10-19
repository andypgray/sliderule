using Shouldly;
using SlideRule.Checking;
using SlideRule.Cli.Diff;
using SlideRule.Roslyn;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     <see cref="GitChangedFiles" /> pure parse/compose plumbing, plus real-git pins of the loud-failure
///     contract, of which files a resolve lists, and of the index it leaves untouched. The NUL parse tolerates
///     a missing trailing separator; compose rebases toplevel-relative paths onto absolute forward-slash paths
///     and dedupes; running outside any repo throws a <see cref="UserErrorException" /> (which the CLI maps to
///     exit 2).
/// </summary>
public sealed class GitChangedFilesTests
{
    // ComposeAbsolute runs Path.GetFullPath, which only leaves the toplevel untouched when it is already
    // rooted for the host OS — a Windows drive letter off Windows would get the CI working directory
    // prepended. Root the vector natively so the pins hold on every platform (production always gets a
    // real absolute toplevel from `git rev-parse --show-toplevel`).
    private static readonly string Toplevel = OperatingSystem.IsWindows() ? "C:/repo" : "/repo";

    [Fact]
    public void ParseZTerminated_EmptyOutput_IsEmpty()
    {
        GitChangedFiles.ParseZTerminated("")
            .ShouldBeEmpty();
    }

    [Fact]
    public void ParseZTerminated_SplitsOnNulAndDropsTrailingEmpty()
    {
        GitChangedFiles.ParseZTerminated("a\0b\0")
            .ShouldBe(["a", "b"]);
    }

    [Fact]
    public void ParseZTerminated_ToleratesMissingTrailingNul()
    {
        GitChangedFiles.ParseZTerminated("a\0b")
            .ShouldBe(["a", "b"]);
    }

    [Fact]
    public void ComposeAbsolute_RebasesOntoToplevelWithForwardSlashes()
    {
        GitChangedFiles.ComposeAbsolute(Toplevel, ["App/Foo.cs", "App/Bar.cs"])
            .ShouldBe([$"{Toplevel}/App/Foo.cs", $"{Toplevel}/App/Bar.cs"]);
    }

    [Fact]
    public void ComposeAbsolute_DedupesRepeatedPaths()
    {
        GitChangedFiles.ComposeAbsolute(Toplevel, ["App/Foo.cs", "App/Foo.cs"])
            .ShouldBe([$"{Toplevel}/App/Foo.cs"]);
    }

    [Fact]
    public async Task ResolveAsync_OutsideAnyRepo_ThrowsUserError()
    {
        using TempDirectory temp = TestTempRoot.Fresh("git-changed-files");

        await Should.ThrowAsync<UserErrorException>(() => GitChangedFiles.ResolveAsync("HEAD", temp.Path, Ct));
    }

    [Fact]
    public async Task ResolveAsync_UntrackedFileWithANonAsciiName_ResolvesToThatFile()
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("git-changed-files");
        string dir = temp.Path;
        GitCommand.Run(dir, "init");
        GitCommand.Commit(dir, "--allow-empty", "-m", "baseline");
        string file = Path.Combine(dir, "Caf\u00e9.cs");
        File.WriteAllText(file, "public sealed class Cafe;\n");

        // Act
        DiffContext context = await GitChangedFiles.ResolveAsync("HEAD", dir, Ct);

        // Assert
        context.Contains(file)
            .ShouldBeTrue("git writes the name as UTF-8, and only a UTF-8 read turns it back into the file.");
    }

    [Fact]
    public async Task ResolveAsync_StatDirtyTrackedFile_LeavesTheIndexAsItWas()
    {
        // Arrange: a tracked file unchanged in content but stale in the index's cached stat, which is what a
        // worktree diff refreshes and writes back.
        using TempDirectory temp = TestTempRoot.Fresh("git-changed-files");
        string dir = temp.Path;
        string file = CommitFile(dir, "Probe.cs");
        FixtureEdits.MakeGitIndexStatStale(file);
        string index = Path.Combine(dir, ".git", "index");
        byte[] before = File.ReadAllBytes(index);

        // Act
        await GitChangedFiles.ResolveAsync("HEAD", dir, Ct);

        // Assert
        File.ReadAllBytes(index)
            .ShouldBe(
                before,
                "Resolving the changed files must not refresh and rewrite the index: that takes index.lock from "
                + "under a commit made at the same moment.");
    }

    [Fact]
    public async Task ResolveAsync_StatDirtyTrackedFile_IsNotListed()
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("git-changed-files");
        string dir = temp.Path;
        string file = CommitFile(dir, "Probe.cs");
        FixtureEdits.MakeGitIndexStatStale(file);

        // Act
        DiffContext context = await GitChangedFiles.ResolveAsync("HEAD", dir, Ct);

        // Assert
        context.Contains(file)
            .ShouldBeFalse("A file whose cached stat is stale but whose content is unchanged has not changed.");
    }

    [Fact]
    public async Task ResolveAsync_FileCommittedSinceTheRef_IsListed()
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("git-changed-files");
        string dir = temp.Path;
        GitCommand.Run(dir, "init");
        GitCommand.Commit(dir, "--allow-empty", "-m", "baseline");
        GitCommand.Run(dir, "tag", "base");
        string file = CommitFile(dir, "Probe.cs");

        // Act
        DiffContext context = await GitChangedFiles.ResolveAsync("base", dir, Ct);

        // Assert
        context.Contains(file)
            .ShouldBeTrue("A file committed after the ref has changed since it.");
    }

    [Fact]
    public async Task ResolveAsync_StagedAndUnstagedEdits_AreListed()
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("git-changed-files");
        string dir = temp.Path;
        string staged = CommitFile(dir, "Staged.cs");
        string unstaged = CommitFile(dir, "Unstaged.cs");
        File.AppendAllText(staged, "// staged\n");
        GitCommand.Run(dir, "add", "Staged.cs");
        File.AppendAllText(unstaged, "// unstaged\n");

        // Act
        DiffContext context = await GitChangedFiles.ResolveAsync("HEAD", dir, Ct);

        // Assert
        context.Contains(staged)
            .ShouldBeTrue("A staged edit has changed against the ref.");
        context.Contains(unstaged)
            .ShouldBeTrue("An unstaged edit has changed against the ref.");
    }

    [Fact]
    public async Task ResolveAsync_FileChangedSinceTheRefAndChangedBack_IsListed()
    {
        // Arrange: changed and committed after the ref, then put back in the worktree, so ref and worktree agree.
        using TempDirectory temp = TestTempRoot.Fresh("git-changed-files");
        string dir = temp.Path;
        string file = CommitFile(dir, "Probe.cs");
        GitCommand.Run(dir, "tag", "base");
        string original = File.ReadAllText(file);
        File.AppendAllText(file, "// changed\n");
        GitCommand.Run(dir, "add", "Probe.cs");
        GitCommand.Commit(dir, "-m", "change");
        File.WriteAllText(file, original);

        // Act
        DiffContext context = await GitChangedFiles.ResolveAsync("base", dir, Ct);

        // Assert
        context.Contains(file)
            .ShouldBeTrue(
                "The listing is what was committed since the ref together with what changed against HEAD, so a file "
                + "changed and then changed back is listed: a tripwire only warns, and that is the safe direction.");
    }

    // Initialises the repository on first use, then writes and commits one C# file at its root.
    private static string CommitFile(string dir, string name)
    {
        if (!Directory.Exists(Path.Combine(dir, ".git"))) GitCommand.Run(dir, "init");
        string file = Path.Combine(dir, name);
        File.WriteAllText(file, $"public sealed class {Path.GetFileNameWithoutExtension(name)};\n");
        GitCommand.Run(dir, "add", name);
        GitCommand.Commit(dir, "-m", name);

        return file;
    }
}
