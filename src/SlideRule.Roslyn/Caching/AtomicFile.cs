namespace SlideRule.Roslyn.Caching;

/// <summary>
///     Writes or copies a file atomically <em>and</em> durably: the bytes go to a uniquely-named sibling temp
///     file in the target's own directory, are flushed to disk, and the temp is then
///     <see cref="File.Move(string,string,bool)" />d over the target. A reader never sees a half-written file;
///     a crash mid-write leaves either the intact old file or the intact new one — never a truncated one; and,
///     because the flush persists the bytes before the rename, a power loss in the move window cannot leave the
///     renamed target pointing at content still sitting unwritten in the OS cache. The unique temp name (not a
///     fixed <c>.tmp</c>) lets two writers of the same target coexist without clobbering each other's scratch
///     file.
/// </summary>
/// <remarks>
///     Failure semantics are deliberate: this <em>throws</em> rather than swallowing to a bool, because a
///     silently-dropped write to a user-owned, version-controlled file would be a lie. A caller persisting
///     disposable derived data — which is simply rebuilt next run — wraps this and re-swallows the throw
///     itself. The temp file is deleted on the failure path.
///     On Windows the move is retried for under a second while another process holds the target or the temp
///     open, which is a passing hold rather than a failure of the write itself; any other I/O failure surfaces
///     at once.
///     Lives in the Roslyn (net10.0) project because the overwriting
///     <see cref="File.Move(string,string,bool)" /> overload does not exist on Core's netstandard2.0.
/// </remarks>
internal static class AtomicFile
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    // About 750 ms in all. The holds this outlasts are an antivirus or indexer scan of a file seconds old and
    // an editor re-reading a file it has open, each measured in milliseconds; hosted Windows CI lost a
    // baseline add's rewrite to one.
    private static readonly int[] MoveRetryDelaysMs = [50, 100, 200, 400];

    /// <summary>
    ///     Writes <paramref name="bytes" /> to <paramref name="path" /> atomically and durably — a temp
    ///     sibling, flushed to disk, then an overwriting move — creating the target directory if it is absent.
    ///     Throws on any I/O failure, after a best-effort delete of the temp file.
    /// </summary>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        PromoteViaTemp(path, temp => WriteDurably(temp, bytes));
    }

    /// <summary>
    ///     Copies <paramref name="source" /> to <paramref name="destination" /> atomically and durably — into a
    ///     temp sibling, flushed to disk, then an overwriting move — creating the destination directory if it is
    ///     absent. Throws on any I/O failure, after a best-effort delete of the temp file.
    /// </summary>
    public static void Copy(string source, string destination)
    {
        PromoteViaTemp(destination, temp =>
        {
            File.Copy(source, temp, true);
            FlushToDisk(temp);
        });
    }

    // The temp file being a sibling of the target is what makes File.Move a rename within one volume (atomic)
    // rather than a cross-volume copy (not), and the Guid is what lets two writers of one target coexist
    // without clobbering each other's scratch — the class's two guarantees, held in one place so the two verbs
    // cannot spell either differently.
    private static void PromoteViaTemp(string destination, Action<string> produce)
    {
        string fullPath = Path.GetFullPath(destination);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            produce(tempPath);
            MoveOverWithRetry(tempPath, fullPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    // POSIX renames over open files, so there the first attempt is the only one.
    internal static void MoveOverWithRetry(string source, string destination)
    {
        foreach (int delayMs in MoveRetryDelaysMs)
            try
            {
                File.Move(source, destination, true);
                return;
            }
            catch (Exception ex) when (IsHeldOpen(ex, destination))
            {
                Thread.Sleep(delayMs);
            }

        File.Move(source, destination, true);
    }

    // Measured on Windows 10 NTFS: a source held open without FILE_SHARE_DELETE fails the move as a sharing
    // violation, and a destination held open in any sharing mode fails it as access denied. A destination that is
    // a directory or read-only fails as access denied too, and no wait cures it, so that is not retried.
    private static bool IsHeldOpen(Exception exception, string destination)
    {
        return exception switch
        {
            IOException io => io.HResult is SharingViolation or LockViolation,
            UnauthorizedAccessException => File.Exists(destination)
                                           && !File.GetAttributes(destination).HasFlag(FileAttributes.ReadOnly),
            _ => false
        };
    }

    // Streams the bytes to disk and fsyncs them before returning, so the caller's subsequent File.Move promotes
    // content already persisted — not merely handed to the OS cache, which a power loss could still drop. That
    // is why this is not a plain File.WriteAllBytes, which does not fsync. The using declaration closes the
    // stream at method exit, before the caller renames — File.Move needs the file closed on Windows.
    private static void WriteDurably(string path, byte[] bytes)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    // Re-opens an already-written file purely to fsync its bytes (the copy path: File.Copy leaves them in the
    // OS cache). Flush(flushToDisk: true) issues the platform disk sync even with no buffered writes of its own.
    private static void FlushToDisk(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Flush(true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup — the temp file is derived scratch, never surfaced to a caller
        }
    }
}
