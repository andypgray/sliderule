using Shouldly;
using Xunit;
using SlideRule.Cli.Hooks;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     Which code files changed between two snapshots, by their bytes: one row per rule <see cref="TurnChanges" />
///     applies. Digests are spelled as tokens, so a row reads which bytes a file held when. No git and no files; the
///     rows that put the comparison in front of a real repository are in <see cref="HookRunnerTests" />.
/// </summary>
public sealed class TurnChangesTests
{
    private const string Head = "h1";
    private const string MovedHead = "h2";

    [Fact]
    public void SameBytes_SameHead_NothingChanged()
    {
        // Arrange: a file left dirty by an earlier turn, rewritten or touched since, holding the same bytes.
        TreeSnapshot before = Snapshot(Head, ("src/A.cs", "a1"));
        TreeSnapshot after = Snapshot(Head, ("src/A.cs", "a1"));

        // Act
        IReadOnlyList<string>? changed = TurnChanges.Between(before, after, null, NoDiskRead);

        // Assert
        changed.ShouldBe([], "Bytes that did not change are not a change, whatever happened to the file's timestamp.");
    }

    [Fact]
    public void NewBytes_InARecordedFile_Changed()
    {
        // Act
        IReadOnlyList<string>? changed = TurnChanges.Between(
            Snapshot(Head, ("src/A.cs", "a1"), ("src/B.cs", "b1")),
            Snapshot(Head, ("src/A.cs", "a2"), ("src/B.cs", "b1")),
            null,
            NoDiskRead);

        // Assert
        changed.ShouldBe(["src/A.cs"], "Only the file whose bytes moved changed; the other dirty file did not.");
    }

    [Fact]
    public void AFileClean_Before_AndListedNow_Changed()
    {
        // Act
        IReadOnlyList<string>? changed = TurnChanges.Between(
            Snapshot(Head), Snapshot(Head, ("src/New.cs", "n1")), null, NoDiskRead);

        // Assert
        changed.ShouldBe(["src/New.cs"], "A file that was HEAD's version and no longer is has changed.");
    }

    [Fact]
    public void ARecordedFile_BackToHeadsVersion_ComparedByItsBytesOnDisk()
    {
        // Arrange: listed before, clean now. Nothing in the later snapshot holds its bytes, so the disk is read.
        TreeSnapshot before = Snapshot(Head, ("src/A.cs", "a1"));
        TreeSnapshot after = Snapshot(Head);

        // Act
        IReadOnlyList<string>? changed = TurnChanges.Between(before, after, null, static _ => "head bytes");

        // Assert
        changed.ShouldBe(["src/A.cs"], "A dirty file put back to HEAD's version changed its bytes.");
    }

    [Fact]
    public void ACommitOfCodeAsItStood_NothingChanged()
    {
        // Arrange: another session commits the dirty file as it is, so HEAD moves and git lists it no longer.
        TreeSnapshot before = Snapshot(Head, ("src/A.cs", "a1"));
        TreeSnapshot after = Snapshot(MovedHead);

        // Act
        IReadOnlyList<string>? changed = TurnChanges.Between(before, after, ["src/A.cs"], static _ => "a1");

        // Assert
        changed.ShouldBe([], "The commit took the bytes in as they stood, so the tree holds the same code it did.");
    }

    [Fact]
    public void ACommitThatChangedACleanFile_Changed()
    {
        // Act: a HEAD move that brought new code, such as a branch integrated into this checkout.
        IReadOnlyList<string>? changed = TurnChanges.Between(
            Snapshot(Head), Snapshot(MovedHead), ["src/Brought.cs", "docs/notes.md"], NoDiskRead);

        // Assert
        changed.ShouldBe(
            ["src/Brought.cs"], "A clean file a commit changed holds new bytes; a file that is not code never counts.");
    }

    [Fact]
    public void AMovedHead_GitCouldNotSay_IsDoubt()
    {
        // Act
        IReadOnlyList<string>? changed = TurnChanges.Between(Snapshot(Head), Snapshot(MovedHead), null, NoDiskRead);

        // Assert
        changed.ShouldBeNull("With HEAD moved and no list of what its commits changed, nothing can be ruled out.");
    }

    [Fact]
    public void AnUnknownTree_IsDoubt()
    {
        // Act
        IReadOnlyList<string>? changed = TurnChanges.Between(TreeSnapshot.Unknown, Snapshot(Head), null, NoDiskRead);

        // Assert
        changed.ShouldBeNull("A tree git could not describe matches nothing, so the stop checks.");
    }

    [Theory]
    [InlineData(TreeSnapshot.Unreadable, "a1")]
    [InlineData("a1", TreeSnapshot.Unreadable)]
    public void AnUnreadableFile_Changed(string was, string now)
    {
        // Act
        IReadOnlyList<string>? changed = TurnChanges.Between(
            Snapshot(Head, ("src/A.cs", was)), Snapshot(Head, ("src/A.cs", now)), null, NoDiskRead);

        // Assert
        changed.ShouldBe(["src/A.cs"], "Bytes that could not be read cannot be shown equal, so the file reads as changed.");
    }

    [Fact]
    public void ADeletionThatStands_NothingChanged()
    {
        // Act
        IReadOnlyList<string>? changed = TurnChanges.Between(
            Snapshot(Head, ("src/Gone.cs", TreeSnapshot.Absent)),
            Snapshot(Head, ("src/Gone.cs", TreeSnapshot.Absent)),
            null,
            NoDiskRead);

        // Assert
        changed.ShouldBe([], "A file already deleted when the turn began, and still deleted, is not the turn's change.");
    }

    private static TreeSnapshot Snapshot(string head, params (string Path, string Digest)[] files)
    {
        return TreeSnapshot.Recorded(head, files);
    }

    // The rows whose later snapshot names every file the comparison needs must never read the disk.
    private static string NoDiskRead(string path)
    {
        throw new InvalidOperationException($"The comparison read {path} from disk, which this row never needs.");
    }
}
