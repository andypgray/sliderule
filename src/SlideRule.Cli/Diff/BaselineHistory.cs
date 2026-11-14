using System.Globalization;
using System.Text;
using SlideRule.Baselines;
using SlideRule.Internal;

namespace SlideRule.Cli.Diff;

/// <summary>
///     Reads when each baseline entry was recorded, and by whom, from the git history of the JSON files under a
///     solution directory and the baseline files its spec declares. The baseline format carries no date, so the
///     history is the only record of one.
/// </summary>
/// <remarks>
///     <para>
///         An entry is traced by its rule ID and identity, never by its line, seal, layout or file: a site-count
///         reduction re-seals its line, a rename moves it to another file, and a format upgrade rewrites every line,
///         and none of those is a new entry. Each commit's live set is every entry of every baseline document in that
///         commit's tree. An entry that joins the set takes that commit's committer date and author, an entry that
///         stays keeps them, and an entry that leaves loses them, so debt paid off and later re-added dates from the
///         re-add. In a commit where a rule's section leaves the set entirely, an identity it held that joins the set
///         under another rule keeps its date, because that is what <c>baseline rename</c> writes. A section that is
///         present with no entries still holds its rule, and lends nothing.
///     </para>
///     <para>
///         The history is HEAD's first-parent line, so an age means "since the entry reached this branch", and a merge
///         that resolves a baseline file is read against its first parent. Every JSON file is read whatever its path,
///         and the ones that do not parse as baseline documents are left out, so a file that moved or was renamed is
///         followed without being named. The scope is the solution directory, not the repository: another tree in the
///         repository that holds the same rule IDs, such as a copy of the solution, would otherwise lend it history. A
///         declared file elsewhere in the work tree, or with another suffix, is read at its declared path alone.
///     </para>
///     <para>
///         The cost is three git processes, one after another, under <see cref="GitProcess" />'s bound each, and any
///         failure, a timeout included, reads as <see cref="BaselineAges.Unavailable" />.
///     </para>
/// </remarks>
internal static class BaselineHistory
{
    private const char RecordSeparator = '\x1e';
    private const char FieldSeparator = '\x1f';

    // ParseLog slices the output under exactly these flags: one record per commit, its header fields, then raw diff
    // pairs of a status line and a path, all NUL-separated. The pathspecs follow.
    private static readonly string[] LogArguments =
    [
        "log", "--first-parent", "--reverse", "-m", "--no-renames", "--raw", "--no-abbrev", "-z", "--no-color",
        "--no-show-signature", "--format=%x1e%H%x1f%cI%x1f%an%x1f%P", "--", "*.json"
    ];

    /// <summary>
    ///     The ages of the baseline entries under <paramref name="directory" /> and in
    ///     <paramref name="baselineFiles" />, the absolute paths the spec's rules declare, as the git history records
    ///     them. A declared file outside the work tree has no history.
    /// </summary>
    public static async Task<BaselineAges> ReadAsync(
        string directory, IEnumerable<string> baselineFiles, CancellationToken ct)
    {
        string[]? repository = await GitProcess.AnswerLinesAsync(
            directory, ct, "rev-parse", "--is-shallow-repository", "--show-toplevel");
        if (repository is not [var shallow, var topLevel, ..]) return BaselineAges.Unavailable;

        string[] arguments = [.. LogArguments, .. DeclaredOutsideTheGlob(directory, topLevel, baselineFiles)];
        string? log = await GitProcess.AnswerAsync(directory, ct, arguments);
        if (log is null || ParseLog(log) is not { } commits) return BaselineAges.Unavailable;

        string[] blobIds = commits
            .SelectMany(commit => commit.Changes)
            .Select(change => change.Blob)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        IReadOnlyDictionary<string, string>? blobs = await ReadBlobsAsync(directory, blobIds, ct);
        if (blobs is null) return BaselineAges.Unavailable;

        return Replay(commits, blobs, shallow == "true");
    }

    // A declared file the glob misses, because it lies outside the directory or is not named *.json, as a literal
    // pathspec relative to the directory. One outside the work tree is left out, since git refuses the whole log
    // over a pathspec it cannot reach; it is compared as spelled, so a link or a substituted drive leaves it out too.
    private static IEnumerable<string> DeclaredOutsideTheGlob(
        string directory, string topLevel, IEnumerable<string> baselineFiles)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(topLevel)) + Path.DirectorySeparatorChar;
        string under = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return baselineFiles
            .Select(Path.GetFullPath)
            .Where(file => file.StartsWith(root, PathComparison.Comparison))
            .Where(file => !file.StartsWith(under, PathComparison.Comparison)
                           || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(file => ":(literal)" + Path.GetRelativePath(Path.GetFullPath(directory), file).Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    ///     The commits of a <see cref="LogArguments" /> listing, oldest first, or null when the text is not one.
    /// </summary>
    internal static IReadOnlyList<HistoryCommit>? ParseLog(string log)
    {
        var commits = new List<HistoryCommit>();
        foreach (string record in log.Split(RecordSeparator))
        {
            if (record.Length == 0) continue;

            // The header ends at the first NUL; after it come the raw pairs, the first behind a line break.
            int headerEnd = record.IndexOf('\0');
            string header = headerEnd < 0 ? record : record[..headerEnd];
            string[] fields = header.Split(FieldSeparator);
            if (fields.Length != 4) return null;

            bool dated = DateTimeOffset.TryParse(
                fields[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset committedAt);
            if (!dated) return null;

            string body = headerEnd < 0 ? "" : record[(headerEnd + 1)..];
            if (ChangesIn(body) is not { } changes) return null;

            var commit = new HistoryCommit(new EntryRecord(committedAt, fields[2]), fields[3].Length > 0, changes);
            commits.Add(commit);
        }

        return commits;
    }

    // Each pair is a status line, ":<old mode> <new mode> <old blob> <new blob> <status>", then the path.
    private static IReadOnlyList<HistoryChange>? ChangesIn(string body)
    {
        string[] tokens = body
            .Split('\0')
            .Select(token => token.TrimStart('\n'))
            .Where(token => token.Length > 0)
            .ToArray();
        if (tokens.Length % 2 != 0) return null;

        var changes = new List<HistoryChange>();
        for (var i = 0; i < tokens.Length; i += 2)
        {
            string[] status = tokens[i].Split(' ');
            if (status is not [[':', ..], _, _, _, _]) return null;

            changes.Add(new HistoryChange(tokens[i + 1], IsFileBlob(status[1], status[3]) ? status[3] : null));
        }

        return changes;
    }

    // A deletion names the all-zero blob, and a symbolic link or a submodule names no JSON text.
    private static bool IsFileBlob(string mode, string blob)
    {
        return mode is "100644" or "100755" && blob.Any(character => character != '0');
    }

    /// <summary>
    ///     Replays <paramref name="commits" />, oldest first, over the baseline documents among
    ///     <paramref name="blobs" />, keyed by blob ID.
    /// </summary>
    internal static BaselineAges Replay(
        IReadOnlyList<HistoryCommit> commits, IReadOnlyDictionary<string, string> blobs, bool isShallow)
    {
        var files = new Dictionary<string, BaselineBlob>(StringComparer.Ordinal);
        var recordings = new Dictionary<BaselineAges.EntryKey, BaselineAges.Recording>();
        var parsed = new Dictionary<string, BaselineBlob?>(StringComparer.Ordinal);

        foreach (HistoryCommit commit in commits)
        {
            foreach (HistoryChange change in commit.Changes)
            {
                BaselineBlob? file = change.Blob is { } blob ? BaselineOf(blob, blobs, parsed) : null;
                if (file is null)
                    files.Remove(change.Path);
                else
                    files[change.Path] = file;
            }

            Step(recordings, files, new BaselineAges.Recording(commit.Record, !commit.HasParents));
        }

        return BaselineAges.Of(recordings, isShallow);
    }

    private static void Step(
        Dictionary<BaselineAges.EntryKey, BaselineAges.Recording> recordings,
        Dictionary<string, BaselineBlob> files,
        BaselineAges.Recording thisCommit)
    {
        HashSet<BaselineAges.EntryKey> live = files.Values
            .SelectMany(file => file.Keys)
            .ToHashSet();
        // A section counts while it is present, emptied or not: accept-reductions keeps an emptied section, and an
        // emptied section has not moved anywhere.
        HashSet<string> liveRules = files.Values
            .SelectMany(file => file.Rules)
            .ToHashSet(StringComparer.Ordinal);

        Dictionary<BaselineAges.EntryKey, BaselineAges.Recording> left = recordings
            .Where(recorded => !live.Contains(recorded.Key))
            .ToDictionary(recorded => recorded.Key, recorded => recorded.Value);
        foreach (BaselineAges.EntryKey key in left.Keys) recordings.Remove(key);

        // Only a section that left whole can lend its dates, and where several did, the oldest date wins.
        List<KeyValuePair<BaselineAges.EntryKey, BaselineAges.Recording>> renamedAway = left
            .Where(recorded => !liveRules.Contains(recorded.Key.RuleId))
            .ToList();

        foreach (BaselineAges.EntryKey key in live)
        {
            if (recordings.ContainsKey(key)) continue;

            BaselineAges.Recording? carried = renamedAway
                .Where(recorded => recorded.Key.Identity.Equals(key.Identity))
                .OrderBy(recorded => recorded.Value.Record.RecordedAt)
                .ThenBy(recorded => recorded.Key.RuleId, StringComparer.Ordinal)
                .Select(recorded => recorded.Value)
                .FirstOrDefault();
            recordings[key] = carried ?? thisCommit;
        }
    }

    // A blob is parsed once however many commits name it, and one that is not a baseline document is null. It is
    // read without its seals, so a hand-edited or old version still names its entries.
    private static BaselineBlob? BaselineOf(
        string blob, IReadOnlyDictionary<string, string> blobs, Dictionary<string, BaselineBlob?> parsed)
    {
        if (parsed.TryGetValue(blob, out BaselineBlob? known)) return known;

        BaselineDocument? document = blobs.TryGetValue(blob, out string? text) && LooksLikeBaseline(text)
            ? BaselineDocumentReader.Read(text, verifySeals: false).Document
            : null;
        BaselineBlob? file = document is null
            ? null
            : new BaselineBlob(
                document.Sections.Keys.ToList(),
                document.Sections
                    .SelectMany(section => section.Value.Select(entry => new BaselineAges.EntryKey(section.Key, entry)))
                    .ToList());

        parsed[blob] = file;
        return file;
    }

    // Two names every baseline document carries, so a large JSON file of another kind is never parsed whole.
    private static bool LooksLikeBaseline(string text)
    {
        return text.Contains("\"schemaVersion\"", StringComparison.Ordinal)
               && text.Contains("\"rules\"", StringComparison.Ordinal);
    }

    // One cat-file process for every blob. Its output is a header line, "<id> blob <size>", then that many bytes and
    // a line feed, per blob; a missing object is a header alone, "<id> missing".
    private static async Task<IReadOnlyDictionary<string, string>?> ReadBlobsAsync(
        string directory, string[] blobIds, CancellationToken ct)
    {
        if (blobIds.Length == 0) return new Dictionary<string, string>(StringComparer.Ordinal);

        string input = string.Concat(blobIds.Select(id => id + "\n"));
        byte[]? output = await GitProcess.AnswerBytesAsync(directory, input, ct, "cat-file", "--batch");
        return output is null ? null : ParseBatch(output);
    }

    /// <summary>
    ///     The blobs of a <c>cat-file --batch</c> answer, keyed by ID, or null when the bytes are not one.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? ParseBatch(byte[] output)
    {
        var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
        var position = 0;
        while (position < output.Length)
        {
            int lineEnd = Array.IndexOf(output, (byte)'\n', position);
            if (lineEnd < 0) return null;

            string[] header = Encoding.ASCII.GetString(output, position, lineEnd - position).Split(' ');
            position = lineEnd + 1;
            if (header is [_, "missing"]) continue;

            if (header.Length != 3
                || !int.TryParse(header[2], NumberStyles.None, CultureInfo.InvariantCulture, out int size)
                || position + size > output.Length)
                return null;

            blobs[header[0]] = Utf8Text(output, position, size);
            position += size + 1;
        }

        return blobs;
    }

    // A byte order mark is cut as a read from disk cuts it, so a blob reads as its checked-out file does.
    private static string Utf8Text(byte[] bytes, int start, int count)
    {
        string text = Encoding.UTF8.GetString(bytes, start, count);
        return text.StartsWith('﻿') ? text[1..] : text;
    }

    /// <summary>
    ///     One commit of the history: its date and author, whether it has a parent, and the JSON files it changed.
    /// </summary>
    internal sealed record HistoryCommit(EntryRecord Record, bool HasParents, IReadOnlyList<HistoryChange> Changes);

    /// <summary>A baseline document's rule sections, emptied ones included, and the entries they hold.</summary>
    private sealed record BaselineBlob(IReadOnlyList<string> Rules, IReadOnlyList<BaselineAges.EntryKey> Keys);

    /// <summary>
    ///     A file a commit changed, and its new blob, or null where the commit deleted it or it is not a file.
    /// </summary>
    internal sealed record HistoryChange(string Path, string? Blob);
}
