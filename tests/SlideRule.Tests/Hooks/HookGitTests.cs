using Shouldly;
using Xunit;
using SlideRule.Cli.Hooks;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     The pure halves of the hook's git questions: the parsing, digesting and path arithmetic that need no
///     repository. No git runs here; the rows that need a real repository are in <see cref="HookRunnerTests" />.
/// </summary>
public sealed class HookGitTests
{
    [Fact]
    public void Of_KeepsTheCodeEntriesInGitsOrder()
    {
        // Act
        TreeSnapshot snapshot = SnapshotOf("?? src/A.cs\0 M README.md\0M  src/B.csproj\0");

        // Assert
        snapshot.Paths.ShouldBe(
            ["src/A.cs", "src/B.csproj"], "Only the code entries are kept, in the order git listed them.");
        snapshot.DigestOf("src/B.csproj")
            .ShouldBe("digest of src/B.csproj", "Each kept file carries the digest of its bytes.");
        snapshot.DigestOf("README.md")
            .ShouldBeNull("A file that is not code is not part of the snapshot at all.");
    }

    [Fact]
    public void Paths_OfAnUnstagedEdit_CutAfterAStatusThatStartsWithASpace()
    {
        // Arrange: a tracked file changed only in the work tree has the status field " M".
        TreeSnapshot snapshot = SnapshotOf(" M src/C.cs\0");

        // Act
        IReadOnlyList<string> paths = snapshot.Paths;

        // Assert
        paths.ShouldBe(["src/C.cs"], "A leading space is still one of the three characters before the path.");
    }

    [Theory]
    [InlineData("src/Caf\u00e9.cs")]
    [InlineData("src/quo\"te.cs")]
    [InlineData("src/back\\slash.cs")]
    [InlineData("src/with space.cs")]
    // An arrow inside a name is part of the name: with renames off, no entry carries a separator.
    [InlineData("src/a -> b.cs")]
    public void Paths_NameEachFileAsWritten(string name)
    {
        // Arrange
        TreeSnapshot snapshot = SnapshotOf($"?? {name}\0");

        // Act
        IReadOnlyList<string> paths = snapshot.Paths;

        // Assert
        paths.ShouldBe(
            [name],
            "With -z git prints a path as written, so the snapshot has to hand it back unchanged to name the file.");
    }

    [Fact]
    public void Of_ACodePathWithALineBreak_IsUnknown()
    {
        // Act
        TreeSnapshot snapshot = SnapshotOf("?? src/a\nb.cs\0");

        // Assert
        snapshot.Known.ShouldBeFalse(
            "A record of one file per line cannot carry a name with a line break, so the tree is unknown and checked.");
    }

    [Fact]
    public void Digest_SameBytes_SameDigest_AndAMissingFileIsAbsent()
    {
        // Arrange
        string directory = Path.Combine(TestTempRoot.For("hook-digest"), Guid.NewGuid()
            .ToString("N"));
        Directory.CreateDirectory(directory);
        string first = Path.Combine(directory, "First.cs");
        string second = Path.Combine(directory, "Second.cs");
        File.WriteAllText(first, "public sealed class Probe;\n");
        File.WriteAllText(second, "public sealed class Probe;\n");

        // Act
        string firstDigest = TreeSnapshot.Digest(first);
        string secondDigest = TreeSnapshot.Digest(second);
        File.AppendAllText(second, "// one more line\n");
        string changedDigest = TreeSnapshot.Digest(second);

        // Assert
        firstDigest.ShouldBe(secondDigest, "A digest is of the bytes alone, so two files that hold the same bytes agree.");
        firstDigest.ShouldMatch("^[0-9a-f]{64}$", "A digest is lowercase hex SHA-256, which the state record parses.");
        changedDigest.ShouldNotBe(firstDigest, "Changed bytes change the digest.");
        TreeSnapshot.Digest(Path.Combine(directory, "Missing.cs"))
            .ShouldBe(TreeSnapshot.Absent, "A listed file that is not there is a deletion, digested as absent.");
    }

    // A snapshot of a status listing whose files are digested by name, so a row reads which file a digest belongs to.
    private static TreeSnapshot SnapshotOf(string status)
    {
        return TreeSnapshot.Of("abc123", "/repo", status, static path => $"digest of {path}");
    }

    [Theory]
    [InlineData("src/A.cs", true)]
    [InlineData("?? src/A.cs", true)]
    [InlineData("src/A.CS", true)]
    [InlineData("src/App.slnx", true)]
    [InlineData("Probe.csx", false)]
    [InlineData("src/A.cs\"", false)]
    public void IsCode_OnlyTheEndCounts(string pathOrEntry, bool expected)
    {
        // Act
        bool isCode = HookCodeFiles.IsCode(pathOrEntry);

        // Assert
        isCode.ShouldBe(expected, $"Only the extension at the very end of '{pathOrEntry}' decides whether it is code.");
    }

    [Fact]
    public void GitDirectoryIdentity_RelativeAnswer_MatchesTheLinkedWorktreesAbsoluteOne()
    {
        // Arrange: the main checkout answers relative to itself, and a linked worktree answers absolutely.
        string top = Path.Combine(Path.GetTempPath(), "repo");
        string worktree = Path.Combine(top, ".claude", "worktrees", "w1");

        // Act
        string fromMain = HookGit.GitDirectoryIdentity(".git", top);
        string fromWorktree = HookGit.GitDirectoryIdentity(Path.Combine(top, ".git"), worktree);

        // Assert
        fromMain.ShouldBe(
            fromWorktree, "Both trees share one git directory, so both answers must name it the same way.");
    }

    [Fact]
    public void GitDirectoryIdentity_DotSegments_Normalise()
    {
        // Arrange
        string top = Path.Combine(Path.GetTempPath(), "repo");

        // Act
        string dotted = HookGit.GitDirectoryIdentity("sub/../.git", top);
        string plain = HookGit.GitDirectoryIdentity(".git", top);

        // Assert
        dotted.ShouldBe(plain, "A dot segment names the same directory, so it must not make a second identity.");
    }

    [Fact]
    public void GitDirectoryIdentity_Case_FollowsTheOsRule()
    {
        // Arrange
        string top = Path.Combine(Path.GetTempPath(), "repo");

        // Act
        string lower = HookGit.GitDirectoryIdentity(".git", top);
        string upper = HookGit.GitDirectoryIdentity(".GIT", top.ToUpperInvariant());

        // Assert
        string.Equals(lower, upper, StringComparison.Ordinal)
            .ShouldBe(
                !OperatingSystem.IsLinux(),
                "Windows and macOS ignore case in a path, so two spellings are one directory there; Linux does not.");
    }
}
