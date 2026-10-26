using Shouldly;
using SlideRule.Roslyn.Caching;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Caching;

/// <summary>
///     <see cref="AtomicFile" />: the committed-file atomic writer. Proves the observable contract over
///     scratch temp dirs — a successful write leaves exactly the target (the temp sibling is renamed onto
///     it, never left behind), an existing target is overwritten in place, a missing target directory is
///     created first, and on Windows a brief hold on the target or the temp by another process is waited out.
/// </summary>
public sealed class AtomicFileTests
{
    [Fact]
    public void WriteAllBytes_SuccessfulWrite_LeavesOnlyTheTargetNoTempSibling()
    {
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string path = temp.PathOf("target.bin");

        AtomicFile.WriteAllBytes(path, [1, 2, 3]);

        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
        // The temp sibling is moved onto the target, never abandoned: the directory holds exactly it.
        FileNamesIn(temp.Path)
            .ShouldBe(["target.bin"]);
    }

    [Fact]
    public void WriteAllBytes_ExistingTarget_OverwritesInPlaceWithNoLeftoverTemp()
    {
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string path = temp.PathOf("target.bin");
        // Arbitrary filler to be overwritten, not text — a UTF-8 literal of tabs would say the
        // opposite of what these bytes are for.
        // ReSharper disable once UseUtf8StringLiteral
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        AtomicFile.WriteAllBytes(path, [1, 2, 3]);

        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
        FileNamesIn(temp.Path)
            .ShouldBe(["target.bin"]);
    }

    [Fact]
    public void WriteAllBytes_MissingDirectory_CreatesItThenWrites()
    {
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string path = temp.PathOf("nested", "deeper", "target.bin");

        AtomicFile.WriteAllBytes(path, [1, 2, 3]);

        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void WriteAllBytes_TargetPathIsExistingDirectory_ThrowsAndCleansUpTempSibling()
    {
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        // The target path names an existing *directory*: the temp sibling writes fine, but the overwriting
        // File.Move cannot replace a directory with a file, so it throws — walking the catch's
        // TryDelete-then-rethrow leg. The failure must surface (not be swallowed to a bool, unlike the
        // cache stores that wrap this), and the scratch temp file must be deleted, not abandoned.
        string target = temp.PathOf("occupied");
        Directory.CreateDirectory(target);

        Should.Throw<Exception>(() => AtomicFile.WriteAllBytes(target, [1, 2, 3]));

        // TryDelete ran on the failure path: the directory holds only the 'occupied' subdirectory — no
        // leftover "<target>.<guid>.tmp" sibling file.
        FileNamesIn(temp.Path)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Copy_SuccessfulCopy_LeavesOnlyTheTargetNoTempSibling()
    {
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string source = temp.PathOf("source.bin");
        File.WriteAllBytes(source, [1, 2, 3]);
        string path = temp.PathOf("target.bin");

        AtomicFile.Copy(source, path);

        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
        // The temp sibling is moved onto the target, never abandoned: beside the source, the directory holds
        // exactly it — no leftover "<target>.<guid>.tmp".
        FileNamesIn(temp.Path)
            .ShouldBe(["source.bin", "target.bin"]);
    }

    [Fact]
    public void Copy_ExistingTarget_OverwritesInPlaceWithNoLeftoverTemp()
    {
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string source = temp.PathOf("source.bin");
        File.WriteAllBytes(source, [1, 2, 3]);
        string path = temp.PathOf("target.bin");
        // Arbitrary filler to be overwritten, not text — a UTF-8 literal of tabs would say the
        // opposite of what these bytes are for.
        // ReSharper disable once UseUtf8StringLiteral
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        AtomicFile.Copy(source, path);

        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
        FileNamesIn(temp.Path)
            .ShouldBe(["source.bin", "target.bin"]);
    }

    [Fact]
    public void Copy_MissingDirectory_CreatesItThenCopies()
    {
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string source = temp.PathOf("source.bin");
        File.WriteAllBytes(source, [1, 2, 3]);
        string path = temp.PathOf("nested", "deeper", "target.bin");

        AtomicFile.Copy(source, path);

        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Copy_DestinationPathIsExistingDirectory_ThrowsAndCleansUpTempSibling()
    {
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        // The destination path names an existing *directory*: the temp sibling copies fine, but the
        // overwriting File.Move cannot replace a directory with a file, so it throws — walking the catch's
        // TryDelete-then-rethrow leg. The failure must surface (not be swallowed to a bool, unlike the cache
        // stores that wrap this), and the scratch temp file must be deleted, not abandoned.
        string source = temp.PathOf("source.bin");
        File.WriteAllBytes(source, [1, 2, 3]);
        string target = temp.PathOf("occupied");
        Directory.CreateDirectory(target);

        Should.Throw<Exception>(() => AtomicFile.Copy(source, target));

        // TryDelete ran on the failure path: only the source file and the 'occupied' subdirectory remain —
        // no leftover "<target>.<guid>.tmp" sibling.
        FileNamesIn(temp.Path)
            .ShouldBe(["source.bin"]);
    }

    [Fact]
    public void WriteAllBytes_TargetHeldBriefly_WaitsOutTheHoldAndWrites()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file held open.");
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string path = temp.PathOf("target.bin");
        // ReSharper disable once UseUtf8StringLiteral
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        // A reader without FILE_SHARE_DELETE, as an antivirus scan of a fresh file is, that lets go once the
        // first move onto the target has been refused as access denied.
        using FileHold hold = FileHold.WithoutDeleteSharing(path);
        hold.ReleaseOnRefusal(nth: 1);

        AtomicFile.WriteAllBytes(path, [1, 2, 3]);

        hold.Refusals
            .ShouldHaveSingleItem()
            .ShouldBeOfType<UnauthorizedAccessException>();
        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
        FileNamesIn(temp.Path)
            .ShouldBe(["target.bin"]);
    }

    [Fact]
    public void Copy_DestinationHeldBriefly_WaitsOutTheHoldAndCopies()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file held open.");
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string source = temp.PathOf("source.bin");
        File.WriteAllBytes(source, [1, 2, 3]);
        string path = temp.PathOf("target.bin");
        // ReSharper disable once UseUtf8StringLiteral
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        using FileHold hold = FileHold.WithoutDeleteSharing(path);
        hold.ReleaseOnRefusal(nth: 1);

        AtomicFile.Copy(source, path);

        hold.Refusals
            .ShouldHaveSingleItem()
            .ShouldBeOfType<UnauthorizedAccessException>();
        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
        FileNamesIn(temp.Path)
            .ShouldBe(["source.bin", "target.bin"]);
    }

    [Fact]
    public void WriteAllBytes_TargetHeldThroughEveryRetry_ThrowsAndCleansUpTempSibling()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file held open.");
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string path = temp.PathOf("target.bin");
        // ReSharper disable once UseUtf8StringLiteral
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        using (FileHold.WithoutDeleteSharing(path))
        {
            // The retry is bounded: a hold that outlasts it surfaces as the access denial Windows reports for
            // a held destination.
            Should.Throw<UnauthorizedAccessException>(() => AtomicFile.WriteAllBytes(path, [1, 2, 3]));
        }

        // The old content stands, and the temp sibling is deleted, not abandoned.
        // ReSharper disable once UseUtf8StringLiteral
        File.ReadAllBytes(path)
            .ShouldBe([9, 9, 9, 9]);
        FileNamesIn(temp.Path)
            .ShouldBe(["target.bin"]);
    }

    [Fact]
    public void MoveOverWithRetry_SourceHeldBriefly_WaitsOutTheSharingViolationAndMoves()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename a file held open.");
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string source = temp.PathOf("target.bin.scan.tmp");
        File.WriteAllBytes(source, [1, 2, 3]);
        string path = temp.PathOf("target.bin");
        // ReSharper disable once UseUtf8StringLiteral
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        // A scanner reading the freshly written temp without FILE_SHARE_DELETE, that lets go once the first
        // rename has been refused as a sharing violation.
        using FileHold hold = FileHold.WithoutDeleteSharing(source);
        hold.ReleaseOnRefusal(nth: 1);

        AtomicFile.MoveOverWithRetry(source, path);

        var refusal = hold.Refusals
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IOException>();
        refusal.HResult.ShouldBe(unchecked((int)0x80070020));
        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
        FileNamesIn(temp.Path)
            .ShouldBe(["target.bin"]);
    }

    [Fact]
    public void MoveOverWithRetry_SourceHeldThroughFourRefusals_MovesOnTheAttemptAfter()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename a file held open.");
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string source = temp.PathOf("target.bin.scan.tmp");
        File.WriteAllBytes(source, [1, 2, 3]);
        string path = temp.PathOf("target.bin");

        // Four must agree with the length of AtomicFile's retry schedule: a hold that lets go only inside the
        // fourth refused rename still ends in a move, which holds the schedule's length without a clock.
        using FileHold hold = FileHold.WithoutDeleteSharing(source);
        hold.ReleaseOnRefusal(nth: 4);

        AtomicFile.MoveOverWithRetry(source, path);

        const int sharingViolation = unchecked((int)0x80070020);
        hold.Refusals
            .Select(static refusal => refusal.HResult)
            .ShouldBe([sharingViolation, sharingViolation, sharingViolation, sharingViolation]);
        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void MoveOverWithRetry_SourceHeldThroughEveryRetry_ThrowsTheSharingViolation()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename a file held open.");
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string source = temp.PathOf("target.bin.scan.tmp");
        File.WriteAllBytes(source, [1, 2, 3]);
        string path = temp.PathOf("target.bin");

        IOException thrown;
        using (FileHold.WithoutDeleteSharing(source))
        {
            thrown = Should.Throw<IOException>(() => AtomicFile.MoveOverWithRetry(source, path));
        }

        thrown.HResult.ShouldBe(unchecked((int)0x80070020));
        File.Exists(path)
            .ShouldBeFalse();
    }

    // File names in ordinal order, so a listing compares whole whatever order the file system returns.
    private static IReadOnlyList<string?> FileNamesIn(string directory)
    {
        return Directory.GetFiles(directory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
