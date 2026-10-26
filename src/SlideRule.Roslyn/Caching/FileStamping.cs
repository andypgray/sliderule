using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Caching;

/// <summary>
///     The file-stamping primitives of the structure-keyed cache stores, pure over their arguments and disk
///     state, defined once so every store validates a <see cref="FileStamp" /> against disk the same way: stat
///     first, and re-hash only when the stat moved, since a bare mtime touch on unchanged bytes must not
///     invalidate.
/// </summary>
internal static class FileStamping
{
    /// <summary>
    ///     Probe files whose presence anywhere from a project directory up to the filesystem root changes
    ///     the build — stamped even when absent, so a newly-appearing one is an existence flip.
    /// </summary>
    /// <remarks>
    ///     <c>Directory.Packages.props</c> is here on that contract: a central-package-management edit usually
    ///     also rewrites <c>project.assets.json</c>, which <see cref="IntermediateOutputTree.AssetsPathsOf" />
    ///     already stamps, but only after a restore, so that file is a proxy and the probe holds the invariant.
    /// </remarks>
    internal static readonly string[] StructuralProbeFileNames =
        ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json"];

    /// <summary>
    ///     The tool version stamped into a manifest: this (Roslyn) assembly's informational version
    ///     (e.g. <c>0.1.0+&lt;sha&gt;</c>). The .NET SDK appends the commit hash, giving per-commit cache
    ///     invalidation during development. All packages ship lockstep, so the value equals the tool's.
    /// </summary>
    internal static readonly string CurrentToolVersion =
        typeof(FileStamping).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>
    ///     Stamps <paramref name="path" /> as it is on disk now — existence, mtime/length, content hash, and the
    ///     racy-window promotion verdict.
    /// </summary>
    internal static FileStamp StampOf(string path)
    {
        string full = Path.GetFullPath(path);
        FileFreshness fresh = FileFreshness.Capture(full);
        if (!fresh.Exists) return new FileStamp(full, false, 0, 0, null, false);

        return new FileStamp(full, true, fresh.LastWriteTimeUtc.Ticks, fresh.Length, TryHashFile(full), fresh.IsPromoted);
    }

    /// <summary>
    ///     Stamps <paramref name="path" /> as a source document: everything <see cref="StampOf" /> records
    ///     plus the <see cref="SourceShape" /> of the bytes it hashed.
    /// </summary>
    /// <remarks>
    ///     The <see cref="FileFreshness.Capture" /> comes first, exactly as in <see cref="StampOf" />, so the
    ///     racy-window verdict is never made against a stat taken after the read.
    /// </remarks>
    internal static FileStamp StampDocument(string path)
    {
        string full = Path.GetFullPath(path);
        FileFreshness fresh = FileFreshness.Capture(full);
        if (!fresh.Exists) return new FileStamp(full, false, 0, 0, null, false);

        (string Sha256, SourceShape Shape)? read = TryReadDocument(full);
        return new FileStamp(
            full, true, fresh.LastWriteTimeUtc.Ticks, fresh.Length, read?.Sha256, fresh.IsPromoted, read?.Shape);
    }

    /// <summary>
    ///     One read of a C# document: the lowercase-hex SHA-256 of its bytes and the
    ///     <see cref="SourceShape" /> of the text they parse to, or null when it is unreadable — an I/O
    ///     failure degrades exactly as <see cref="TryHashFile" /> does.
    /// </summary>
    /// <remarks>
    ///     Capture and validation both read through here: both halves must come from the same bytes and the same
    ///     parse options on either side, or a shape comparison would be answering about two different texts.
    /// </remarks>
    internal static (string Sha256, SourceShape Shape)? TryReadDocument(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            string sha = Convert.ToHexStringLower(SHA256.HashData(bytes));

            using var stream = new MemoryStream(bytes);
            SourceText text = SourceText.From(stream);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(text, path: path);
            return (sha, SourceShape.Of(tree));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Rebuilds a stamp from a fresh <see cref="FileFreshness" /> capture and an already-computed hash.
    ///     <paramref name="shape" /> is the shape to carry forward, and stays null for a structural input,
    ///     which has none.
    /// </summary>
    internal static FileStamp RefreshStamp(string path, FileFreshness current, string? sha, SourceShape? shape = null)
    {
        return new FileStamp(
            path, current.Exists, current.LastWriteTimeUtc.Ticks, current.Length, sha, current.IsPromoted, shape);
    }

    /// <summary>
    ///     Lowercase-hex SHA-256 of the file's bytes, or null when it is unreadable — an I/O failure degrades, never
    ///     throws. Streamed, so a large input such as the captured binlog is never held whole.
    /// </summary>
    /// <remarks>
    ///     Unbuffered and declared sequential rather than opened through <see cref="File.OpenRead" />: the
    ///     digest already reads front to back in large blocks, so buffering pays nothing here.
    /// </remarks>
    internal static string? TryHashFile(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 0, FileOptions.SequentialScan);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Lowercase-hex SHA-256 of <paramref name="value" />'s UTF-8 bytes — the same digest format as
    ///     <see cref="TryHashFile" />, beside it so the four decisions the two share (algorithm, encoding, hex
    ///     case, no separator) stay one decision. On-disk cache identities are computed with it.
    /// </summary>
    internal static string HashText(string value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>
    ///     Reconstitutes a <see cref="FileFreshness" /> from a stamp for a pure stat comparison
    ///     (<see cref="FileFreshness.MatchesStat" />). The value is never promoted, so
    ///     <see cref="FileFreshness.CanTrust" /> on it trusts nothing: the persisted racy verdict is the
    ///     <see cref="FileStamp.Promoted" /> flag, read through <see cref="IsTrustedBy" />.
    /// </summary>
    internal static FileFreshness ToFreshness(this FileStamp stamp)
    {
        return new FileFreshness(stamp.Exists, new DateTime(stamp.LastWriteTimeUtcTicks, DateTimeKind.Utc), stamp.Length, default);
    }

    /// <summary>
    ///     Whether <paramref name="stamp" /> proves, without a content read, that its file is unchanged against
    ///     <paramref name="current" />, a fresh capture of the same path: the persisted form of
    ///     <see cref="FileFreshness.CanTrust" />.
    /// </summary>
    internal static bool IsTrustedBy(this FileStamp stamp, FileFreshness current)
    {
        return stamp.Promoted && stamp.ToFreshness().MatchesStat(current);
    }

    /// <summary>
    ///     Scalar value-equality of two stamp lists (<see cref="FileStamp" /> is a record) — whether a validation
    ///     refreshed any stamp.
    /// </summary>
    internal static bool StampsEqual(IReadOnlyList<FileStamp> left, IReadOnlyList<FileStamp> right)
    {
        return left.SequenceEqual(right);
    }

    /// <summary>
    ///     Validates every stamp of <paramref name="stamps" /> in order through <see cref="CheckStructural" />,
    ///     returning the stamps to carry forward, or null with <paramref name="changed" /> set to the first stamp
    ///     that changed. <paramref name="onHash" /> is as <see cref="CheckStructural" /> takes it.
    /// </summary>
    internal static IReadOnlyList<FileStamp>? SweepStructural(
        IReadOnlyList<FileStamp> stamps, Action onHash, CancellationToken ct, out FileStamp? changed)
    {
        var refreshedStamps = new List<FileStamp>(stamps.Count);
        foreach (FileStamp stamp in stamps)
        {
            ct.ThrowIfCancellationRequested();
            (bool stampChanged, FileStamp refreshed) = CheckStructural(stamp, onHash);
            if (stampChanged)
            {
                changed = stamp;
                return null;
            }

            refreshedStamps.Add(refreshed);
        }

        changed = null;
        return refreshedStamps;
    }

    /// <summary>
    ///     Validates one structural stamp against disk, returning whether it changed (an existence flip or a real
    ///     content change) and the stamp to carry forward. <paramref name="onHash" /> runs immediately before each
    ///     content hash so a store can count its own reads, and is required so that no caller goes silently
    ///     uncounted.
    /// </summary>
    internal static (bool Changed, FileStamp Refreshed) CheckStructural(FileStamp stamp, Action onHash)
    {
        FileFreshness current = FileFreshness.Capture(stamp.Path);
        if (stamp.Exists != current.Exists) return (true, stamp);
        if (!stamp.Exists) return (false, stamp);
        if (stamp.IsTrustedBy(current)) return (false, stamp);

        onHash();
        string? sha = TryHashFile(stamp.Path);
        if (sha is null || !string.Equals(sha, stamp.Sha256, StringComparison.Ordinal)) return (true, stamp);

        // Content unchanged despite the stat delta (a bare touch): refresh so the next sweep is pure-stat.
        return (false, RefreshStamp(stamp.Path, current, sha));
    }
}
