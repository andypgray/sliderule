using SlideRule.Roslyn;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Mutates a fixture tree the way the suites that exercise reconciliation, caching and git's index need it
///     mutated: an edit the freshness machinery is guaranteed to notice, a tree stamped old enough to be trusted,
///     or a file whose stat git's index no longer matches.
/// </summary>
/// <remarks>
///     <para>
///         <b>The freshness contract the sweep and cache stamps are written against.</b> Both the workspace sweep and
///         the persisted extraction cache decide what to re-read by comparing a file's mtime against the one
///         recorded when it was last seen, and a file stamped close to now is treated as racy — re-read
///         rather than trusted. So the two directions are stamped deliberately, and neither number is
///         arbitrary.
///     </para>
///     <para>
///         <b>Forward, +2s.</b> An edit stamps the future, which guarantees the sweep sees a delta against
///         the load-time fingerprint whatever the filesystem's timestamp granularity — an edit stamped
///         "now" can land inside the same tick as the load and read as unchanged.
///     </para>
///     <para>
///         <b>Backward.</b> Backdating puts a file outside the racy window, so the next capture stamps it
///         promoted — the precondition for the stat fast path. <see cref="BackdateAllDocuments" /> stamps the
///         documents a day back, older than the build outputs a load wrote beside them.
///         <see cref="BackdateTree" /> stamps a whole tree, outputs included, twice the window back and no
///         further: a stamp older than an input outside the tree, such as the targets of an SDK installed
///         minutes earlier, leaves the outputs out of date, and the next design-time build brings them up to
///         date again with a stamp inside the window. Backdating is best-effort: a file that cannot be
///         re-stamped stays racy and re-reads, which costs a read and no correctness.
///     </para>
///     <para>
///         <b>Git's index, -10s.</b> <see cref="MakeGitIndexStatStale" /> serves git's stat cache rather than the
///         sweep's or the cache's, and is not best-effort: a file it cannot re-stamp throws, because one still
///         matching its cached stat would leave the row that uses it asserting nothing.
///     </para>
/// </remarks>
internal static class FixtureEdits
{
    /// <summary>Rewrites a file through <paramref name="transform" />, stamping the edit forward.</summary>
    internal static void EditOnDisk(string path, Func<string, string> transform)
    {
        string content = File.ReadAllText(path);
        File.WriteAllText(path, transform(content));
        StampForward(path);
    }

    /// <summary>Stamps <paramref name="path" />'s write time forward, leaving its bytes as they are.</summary>
    internal static void StampForward(string path)
    {
        // A future mtime guarantees the reconcile sweep sees a delta against the load-time fingerprint.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
    }

    /// <summary>
    ///     Splices <paramref name="member" /> into the (single) class in a source file, just before its
    ///     closing brace, leaving everything after that brace as it was, and stamps the edit forward —
    ///     line-ending agnostic (it only anchors on the final <c>}</c>), so a CRLF or LF fixture checkout both
    ///     work. The member carries its own surrounding newlines.
    /// </summary>
    internal static void InsertMember(string path, string member)
    {
        EditOnDisk(path, source => source.Insert(source.LastIndexOf('}'), member));
    }

    /// <summary>
    ///     Appends <paramref name="member" /> as its own line before a class's final closing brace, then
    ///     re-closes the class and stamps the edit forward — so the member needs no newlines of its own and
    ///     any trailing text after the brace is replaced.
    /// </summary>
    internal static void AppendMemberLine(string path, string member)
    {
        EditOnDisk(path, source => SpliceMemberLine(source, member));
    }

    private static string SpliceMemberLine(string source, string member)
    {
        int lastBrace = source.LastIndexOf('}');
        return source[..lastBrace] + member + "\n}\n";
    }

    /// <summary>
    ///     Stamps every document in <paramref name="snapshot" /> well into the past, so the next sweep
    ///     promotes them instead of re-reading.
    /// </summary>
    internal static void BackdateAllDocuments(WorkspaceSnapshot snapshot)
    {
        DateTime wellPast = DateTime.UtcNow.AddDays(-1);
        IEnumerable<string> paths = snapshot.Solution.Projects
            .SelectMany(p => p.Documents)
            .Select(d => d.FilePath)
            .OfType<string>()
            .Distinct();

        foreach (string path in paths) Backdate(path, wellPast);
    }

    /// <summary>
    ///     Stamps every file under <paramref name="root" /> just past the racy window — the persisted-cache analog
    ///     of <see cref="BackdateAllDocuments" />, over a directory tree rather than a loaded solution.
    /// </summary>
    internal static void BackdateTree(string root)
    {
        DateTime pastRacyWindow = DateTime.UtcNow - FileFreshness.RacyWindow * 2;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) Backdate(file, pastRacyWindow);
    }

    /// <summary>
    ///     Pushes <paramref name="path" />'s write time ten seconds into the past, so git's cached stat for it no
    ///     longer matches whatever the filesystem's timestamp granularity, while its content still does.
    /// </summary>
    internal static void MakeGitIndexStatStale(string path)
    {
        DateTime written = File.GetLastWriteTimeUtc(path);
        File.SetLastWriteTimeUtc(path, written.AddSeconds(-10));
    }

    private static void Backdate(string path, DateTime wellPast)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, wellPast);
        }
        catch (IOException)
        {
            // Best-effort: a file we cannot re-stamp stays racy and simply re-reads, which costs a read.
        }
    }
}
