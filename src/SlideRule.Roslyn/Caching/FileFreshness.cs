namespace SlideRule.Roslyn.Caching;

/// <summary>
///     A file's freshness fingerprint at the moment of capture, and the reasoning about the window in which a
///     timestamp cannot be trusted; what to do about a mismatch is the caller's business.
/// </summary>
/// <remarks>
///     <para>
///         Filesystems record last-write times coarsely, so a fingerprint captured within <see cref="RacyWindow" />
///         of the file's own timestamp cannot prove the file is unchanged: a write in the same tick would share that
///         timestamp. Such a fingerprint stays unpromoted (<see cref="IsPromoted" /> is false) until it has been
///         checked against the file's content once and captured again comfortably outside the window.
///         <see cref="CanTrust" /> insists on promotion and <see cref="MatchesStat" /> does not.
///     </para>
///     <para>
///         Any difference in timestamp counts as a change, including one that moved backwards: a timestamp-preserving
///         restore is a difference, not proof of freshness. <see cref="Length" /> catches a change that put the
///         recorded timestamp back exactly. One case gets past both: equal timestamp, equal length and different
///         content, on an already-promoted file. Nothing here watches the filesystem, so it takes the caller's next
///         wholesale reload, or its next run, to pick such a change up.
///     </para>
/// </remarks>
internal readonly record struct FileFreshness(bool Exists, DateTime LastWriteTimeUtc, long Length, DateTime RecordedAtUtc)
{
    /// <summary>
    ///     The window within which a file's last-write time cannot be trusted on its own: two seconds, which
    ///     covers the FAT/exFAT tick floor and is conservative on NTFS.
    /// </summary>
    public static readonly TimeSpan RacyWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Gets whether this fingerprint was captured more than <see cref="RacyWindow" /> after the file's own
    ///     last-write time, so a later timestamp match can be trusted as proof the file is unchanged.
    /// </summary>
    public bool IsPromoted => Exists && RecordedAtUtc - LastWriteTimeUtc > RacyWindow;

    /// <summary>
    ///     Stats <paramref name="fullPath" /> now, recording the capture instant as the current UTC time, so a
    ///     file last written more than <see cref="RacyWindow" /> ago is captured promoted. A missing file captures
    ///     with <see cref="Exists" /> false, so a file that later appears reads as a change.
    /// </summary>
    public static FileFreshness Capture(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return info.Exists
            ? new FileFreshness(true, info.LastWriteTimeUtc, info.Length, DateTime.UtcNow)
            : new FileFreshness(false, default, 0, DateTime.UtcNow);
    }

    /// <summary>
    ///     Whether <paramref name="current" />, a fresh capture of the same path, has the same existence,
    ///     last-write time and length: a stat comparison only, with no content read and no racy window, for files
    ///     cheap enough to reload wholesale on any difference.
    /// </summary>
    public bool MatchesStat(FileFreshness current)
    {
        return Exists == current.Exists
               && LastWriteTimeUtc == current.LastWriteTimeUtc
               && Length == current.Length;
    }

    /// <summary>
    ///     Whether this fingerprint proves, without a content read, that the file is unchanged against
    ///     <paramref name="current" />, a fresh capture of the same path. Any doubt returns false, so the caller
    ///     reads the file and compares the content itself.
    /// </summary>
    public bool CanTrust(FileFreshness current)
    {
        return Exists
               && current.Exists
               && LastWriteTimeUtc == current.LastWriteTimeUtc
               && Length == current.Length
               && IsPromoted;
    }
}
