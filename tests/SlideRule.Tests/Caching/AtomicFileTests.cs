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
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "target.bin");

            AtomicFile.WriteAllBytes(path, [1, 2, 3]);

            File.ReadAllBytes(path)
                .ShouldBe([1, 2, 3]);
            // The temp sibling is moved onto the target, never abandoned: the directory holds exactly it.
            Directory.GetFiles(dir)
                .Select(Path.GetFileName)
                .ShouldBe(["target.bin"]);
        });
    }

    [Fact]
    public void WriteAllBytes_ExistingTarget_OverwritesInPlaceWithNoLeftoverTemp()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "target.bin");
            // Arbitrary filler to be overwritten, not text — a UTF-8 literal of tabs would say the
            // opposite of what these bytes are for.
            // ReSharper disable once UseUtf8StringLiteral
            File.WriteAllBytes(path, [9, 9, 9, 9]);

            AtomicFile.WriteAllBytes(path, [1, 2, 3]);

            File.ReadAllBytes(path)
                .ShouldBe([1, 2, 3]);
            Directory.GetFiles(dir)
                .Select(Path.GetFileName)
                .ShouldBe(["target.bin"]);
        });
    }

    [Fact]
    public void WriteAllBytes_MissingDirectory_CreatesItThenWrites()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "nested", "deeper", "target.bin");

            AtomicFile.WriteAllBytes(path, [1, 2, 3]);

            File.ReadAllBytes(path)
                .ShouldBe([1, 2, 3]);
        });
    }

    [Fact]
    public void WriteAllBytes_TargetPathIsExistingDirectory_ThrowsAndCleansUpTempSibling()
    {
        WithTempDir(dir =>
        {
            // The target path names an existing *directory*: the temp sibling writes fine, but the overwriting
            // File.Move cannot replace a directory with a file, so it throws — walking the catch's
            // TryDelete-then-rethrow leg. The failure must surface (not be swallowed to a bool, unlike the
            // cache stores that wrap this), and the scratch temp file must be deleted, not abandoned.
            string target = Path.Combine(dir, "occupied");
            Directory.CreateDirectory(target);

            Should.Throw<Exception>(() => AtomicFile.WriteAllBytes(target, [1, 2, 3]));

            // TryDelete ran on the failure path: the directory holds only the 'occupied' subdirectory — no
            // leftover "<target>.<guid>.tmp" sibling file.
            Directory.GetFiles(dir)
                .ShouldBeEmpty();
        });
    }

    [Fact]
    public void Copy_SuccessfulCopy_LeavesOnlyTheTargetNoTempSibling()
    {
        WithTempDir(dir =>
        {
            string source = Path.Combine(dir, "source.bin");
            File.WriteAllBytes(source, [1, 2, 3]);
            string path = Path.Combine(dir, "target.bin");

            AtomicFile.Copy(source, path);

            File.ReadAllBytes(path)
                .ShouldBe([1, 2, 3]);
            // The temp sibling is moved onto the target, never abandoned: beside the source, the directory holds
            // exactly it — no leftover "<target>.<guid>.tmp".
            Directory.GetFiles(dir)
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ShouldBe(["source.bin", "target.bin"]);
        });
    }

    [Fact]
    public void Copy_ExistingTarget_OverwritesInPlaceWithNoLeftoverTemp()
    {
        WithTempDir(dir =>
        {
            string source = Path.Combine(dir, "source.bin");
            File.WriteAllBytes(source, [1, 2, 3]);
            string path = Path.Combine(dir, "target.bin");
            // Arbitrary filler to be overwritten, not text — a UTF-8 literal of tabs would say the
            // opposite of what these bytes are for.
            // ReSharper disable once UseUtf8StringLiteral
            File.WriteAllBytes(path, [9, 9, 9, 9]);

            AtomicFile.Copy(source, path);

            File.ReadAllBytes(path)
                .ShouldBe([1, 2, 3]);
            Directory.GetFiles(dir)
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ShouldBe(["source.bin", "target.bin"]);
        });
    }

    [Fact]
    public void Copy_MissingDirectory_CreatesItThenCopies()
    {
        WithTempDir(dir =>
        {
            string source = Path.Combine(dir, "source.bin");
            File.WriteAllBytes(source, [1, 2, 3]);
            string path = Path.Combine(dir, "nested", "deeper", "target.bin");

            AtomicFile.Copy(source, path);

            File.ReadAllBytes(path)
                .ShouldBe([1, 2, 3]);
        });
    }

    [Fact]
    public void Copy_DestinationPathIsExistingDirectory_ThrowsAndCleansUpTempSibling()
    {
        WithTempDir(dir =>
        {
            // The destination path names an existing *directory*: the temp sibling copies fine, but the
            // overwriting File.Move cannot replace a directory with a file, so it throws — walking the catch's
            // TryDelete-then-rethrow leg. The failure must surface (not be swallowed to a bool, unlike the cache
            // stores that wrap this), and the scratch temp file must be deleted, not abandoned.
            string source = Path.Combine(dir, "source.bin");
            File.WriteAllBytes(source, [1, 2, 3]);
            string target = Path.Combine(dir, "occupied");
            Directory.CreateDirectory(target);

            Should.Throw<Exception>(() => AtomicFile.Copy(source, target));

            // TryDelete ran on the failure path: only the source file and the 'occupied' subdirectory remain —
            // no leftover "<target>.<guid>.tmp" sibling.
            Directory.GetFiles(dir)
                .Select(Path.GetFileName)
                .ShouldBe(["source.bin"]);
        });
    }

    [Fact]
    public async Task WriteAllBytes_TargetHeldBriefly_WaitsOutTheHoldAndWrites()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file held open.");
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string path = Path.Combine(temp.Path, "target.bin");
        // ReSharper disable once UseUtf8StringLiteral
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        // A reader without FILE_SHARE_DELETE, as an antivirus scan of a fresh file is: the first move is
        // refused with a sharing violation, and the hold ends well inside the retry budget.
        FileStream holder = HoldWithoutDeleteSharing(path);
        Task release = ReleaseAfterAsync(holder, TimeSpan.FromMilliseconds(100));

        AtomicFile.WriteAllBytes(path, [1, 2, 3]);
        await release;

        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
        Directory.GetFiles(temp.Path)
            .Select(Path.GetFileName)
            .ShouldBe(["target.bin"]);
    }

    [Fact]
    public async Task Copy_DestinationHeldBriefly_WaitsOutTheHoldAndCopies()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file held open.");
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string source = Path.Combine(temp.Path, "source.bin");
        File.WriteAllBytes(source, [1, 2, 3]);
        string path = Path.Combine(temp.Path, "target.bin");
        // ReSharper disable once UseUtf8StringLiteral
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        FileStream holder = HoldWithoutDeleteSharing(path);
        Task release = ReleaseAfterAsync(holder, TimeSpan.FromMilliseconds(100));

        AtomicFile.Copy(source, path);
        await release;

        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
        Directory.GetFiles(temp.Path)
            .Select(Path.GetFileName)
            .OrderBy(name => name)
            .ShouldBe(["source.bin", "target.bin"]);
    }

    [Fact]
    public void WriteAllBytes_TargetHeldThroughEveryRetry_ThrowsAndCleansUpTempSibling()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file held open.");
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "target.bin");
            // ReSharper disable once UseUtf8StringLiteral
            File.WriteAllBytes(path, [9, 9, 9, 9]);

            using (HoldWithoutDeleteSharing(path))
            {
                // The retry is bounded: a hold that outlasts it surfaces as the access denial Windows reports for
                // a held destination.
                Should.Throw<UnauthorizedAccessException>(() => AtomicFile.WriteAllBytes(path, [1, 2, 3]));
            }

            // The old content stands, and the temp sibling is deleted, not abandoned.
            // ReSharper disable once UseUtf8StringLiteral
            File.ReadAllBytes(path)
                .ShouldBe([9, 9, 9, 9]);
            Directory.GetFiles(dir)
                .Select(Path.GetFileName)
                .ShouldBe(["target.bin"]);
        });
    }

    [Fact]
    public async Task MoveOverWithRetry_SourceHeldBriefly_WaitsOutTheSharingViolationAndMoves()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename a file held open.");
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        string source = Path.Combine(temp.Path, "target.bin.scan.tmp");
        File.WriteAllBytes(source, [1, 2, 3]);
        string path = Path.Combine(temp.Path, "target.bin");
        // ReSharper disable once UseUtf8StringLiteral
        File.WriteAllBytes(path, [9, 9, 9, 9]);

        // A scanner reading the freshly written temp without FILE_SHARE_DELETE: the shape a hosted Windows
        // runner hit, where the rename fails as a sharing violation until the scan lets go.
        FileStream holder = HoldWithoutDeleteSharing(source);
        Task release = ReleaseAfterAsync(holder, TimeSpan.FromMilliseconds(100));

        AtomicFile.MoveOverWithRetry(source, path);
        await release;

        File.ReadAllBytes(path)
            .ShouldBe([1, 2, 3]);
        Directory.GetFiles(temp.Path)
            .Select(Path.GetFileName)
            .ShouldBe(["target.bin"]);
    }

    [Fact]
    public void MoveOverWithRetry_SourceHeldThroughEveryRetry_ThrowsTheSharingViolation()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename a file held open.");
        WithTempDir(dir =>
        {
            string source = Path.Combine(dir, "target.bin.scan.tmp");
            File.WriteAllBytes(source, [1, 2, 3]);
            string path = Path.Combine(dir, "target.bin");

            IOException thrown;
            using (HoldWithoutDeleteSharing(source))
            {
                thrown = Should.Throw<IOException>(() => AtomicFile.MoveOverWithRetry(source, path));
            }

            thrown.HResult.ShouldBe(unchecked((int)0x80070020));
            File.Exists(path)
                .ShouldBeFalse();
        });
    }

    private static FileStream HoldWithoutDeleteSharing(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    // A dedicated thread, never the pool: the retry under test blocks its own thread in Thread.Sleep, and under a
    // whole-suite run a starved pool can schedule a pooled release after the last retry has already failed
    // (seen once: an UnauthorizedAccessException from the final move).
    private static Task ReleaseAfterAsync(FileStream holder, TimeSpan delay)
    {
        return Task.Factory.StartNew(() =>
        {
            Thread.Sleep(delay);
            holder.Dispose();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static void WithTempDir(Action<string> body)
    {
        using TempDirectory temp = TestTempRoot.Fresh("atomic-file");
        body(temp.Path);
    }
}
