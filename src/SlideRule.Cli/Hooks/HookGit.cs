using System.ComponentModel;
using System.Security.Cryptography;
using SlideRule.Cli.Diff;
using SlideRule.Internal;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Hooks;

/// <summary>
///     The git questions <c>sliderule hook</c> asks: which working tree a directory belongs to, which git
///     directory that tree shares, and what HEAD and the changed code files are.
/// </summary>
/// <remarks>
///     Every question has a null answer, and none of them throws. A directory outside any repository, git missing
///     from PATH, and a command that fails or outlives its bound all answer null, and the caller reads null as
///     doubt: where a skip was on offer, the check runs instead.
/// </remarks>
internal static class HookGit
{
    /// <summary>
    ///     The top level of the working tree <paramref name="directory" /> belongs to, as git spells it, or null
    ///     when it belongs to none. A linked worktree answers with its own root.
    /// </summary>
    public static async Task<string?> TopLevelAsync(string directory, CancellationToken ct)
    {
        string? output = await RunAsync(directory, ct, "rev-parse", "--show-toplevel");
        return NonBlank(output);
    }

    /// <summary>
    ///     The top level of the working tree <paramref name="directory" /> belongs to, as git spells it, with the git
    ///     directory every working tree of its repository shares, or null when it belongs to none. One git process
    ///     answers both.
    /// </summary>
    /// <remarks>
    ///     The shared directory is spelled so that two trees of one repository compare equal under the product's
    ///     per-OS path rule. Git names it relative to the directory asked from when it lies near and absolutely
    ///     otherwise, as from a linked worktree, so a relative answer is resolved against that directory, and both
    ///     are normalised. The case is folded by the product's one per-OS path rule, so on Linux the identity is
    ///     case-sensitive. On a case-insensitive mount, an NTFS drive under WSL, two spellings of one repository
    ///     therefore compare as two, as every other path comparison in the product does there.
    /// </remarks>
    public static async Task<(string TopLevel, string SharedGitDirectory)?> TreeIdentityAsync(
        string directory, CancellationToken ct)
    {
        string[] answers = (await RunAsync(directory, ct, "rev-parse", "--show-toplevel", "--git-common-dir") ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (answers.Length != 2) return null;

        string askedFrom = Path.GetFullPath(directory);
        return (answers[0], GitDirectoryIdentity(answers[1], askedFrom));
    }

    /// <summary>
    ///     The root of every working tree of the repository <paramref name="tree" /> belongs to, the main checkout
    ///     and each linked worktree, as git spells them, or null when git could not say.
    /// </summary>
    /// <remarks>
    ///     A worktree git still lists after its directory was deleted is listed here too, so a caller that reads
    ///     one checks that it is there.
    /// </remarks>
    public static async Task<IReadOnlyList<string>?> WorktreesAsync(string tree, CancellationToken ct)
    {
        const string prefix = "worktree ";
        string? output = await RunAsync(tree, ct, "worktree", "list", "--porcelain");
        return output?.Split('\n')
            .Select(static line => line.TrimEnd('\r'))
            .Where(static line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(static line => line[prefix.Length..])
            .ToList();
    }

    /// <summary>
    ///     The git directory <paramref name="common" />, git's <c>rev-parse --git-common-dir</c> answer, names:
    ///     resolved against the fully qualified <paramref name="askedFrom" /> directory and folded by the per-OS
    ///     path rule.
    /// </summary>
    internal static string GitDirectoryIdentity(string common, string askedFrom)
    {
        string full = Path.GetFullPath(common, askedFrom);
        return PathComparison.Fold(full);
    }

    /// <summary>
    ///     HEAD, the top level and the changed code files of the working tree at <paramref name="directory" />, each
    ///     file with the digest of its bytes, or <see cref="TreeSnapshot.Unknown" /> when a question failed.
    /// </summary>
    /// <remarks>
    ///     The files are <c>git status --porcelain=v1 -uall -z --no-renames</c> kept to its code entries, in git's
    ///     order. With <c>-z</c> git prints every path as written, never quoted or escaped, and with renames off a
    ///     rename lists as its deletion and its addition, so a code file renamed to anything else still shows.
    ///     Porcelain paths are relative to the repository root whatever directory asked, which is why the top level
    ///     is asked for beside HEAD: it is where the files are read from.
    /// </remarks>
    public static async Task<TreeSnapshot> SnapshotAsync(string directory, CancellationToken ct)
    {
        // The two questions share nothing and process start-up dominates, so they run together.
        Task<string?> headRun = RunAsync(directory, ct, "rev-parse", "--show-toplevel", "HEAD");
        Task<string?> statusRun = RunAsync(directory, ct, GitChangedFiles.StatusArguments);

        string[] answers = (await headRun ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? status = await statusRun;
        if (answers.Length != 2 || status is null) return TreeSnapshot.Unknown;

        string topLevel = answers[0];
        return TreeSnapshot.Of(answers[1], topLevel, status, path => TreeSnapshot.Digest(Path.Combine(topLevel, path)));
    }

    /// <summary>
    ///     The paths that differ between commits <paramref name="from" /> and <paramref name="to" />, relative to the
    ///     repository root, or null when git could not say. Commit against commit, so the index is never read or
    ///     rewritten, and with renames off a rename names both of its paths.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> CommittedBetweenAsync(
        string directory, string from, string to, CancellationToken ct)
    {
        string? output = await RunAsync(directory, ct, "diff", "--name-only", "-z", "--no-renames", from, to, "--");
        return output is null ? null : GitChangedFiles.ParseZTerminated(output);
    }

    /// <summary>
    ///     Whether <paramref name="path" /> is rooted the way git and a forward-slashed payload spell a root: a
    ///     leading slash, or a drive letter followed by one.
    /// </summary>
    public static bool IsRooted(string path)
    {
        return path.StartsWith('/')
               || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/');
    }

    private static string? NonBlank(string? output)
    {
        string? trimmed = output?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    // The exit code decides, not the output: rev-parse HEAD in a repository with no commit prints "HEAD" and fails.
    private static async Task<string?> RunAsync(string directory, CancellationToken ct, params string[] arguments)
    {
        try
        {
            ChildProcess.ProcessResult result = await GitProcess.RunAsync(directory, ct, arguments);
            return result.ExitCode == 0 ? result.StandardOutput : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException)
        {
            // git is not on PATH, the directory is missing, git would not start, or it outlived its bound: no
            // answer, which reads as doubt.
            return null;
        }
    }
}

/// <summary>
///     A working tree as a prompt or a stop found it: the commit HEAD names, and each code file
///     <c>git status --porcelain=v1 -uall -z --no-renames</c> lists, with a digest of its bytes. Every code file
///     the snapshot does not name was HEAD's version of it, so two snapshots say between them which code changed.
/// </summary>
internal sealed class TreeSnapshot
{
    /// <summary>The digest of a listed file that is not there: a deletion.</summary>
    public const string Absent = "-";

    /// <summary>The digest of an unreadable file, which equals nothing, so the file reads as changed.</summary>
    public const string Unreadable = "?";

    private readonly Dictionary<string, string> _digests;

    private TreeSnapshot(string? head, string? topLevel, IReadOnlyList<(string Path, string Digest)> files)
    {
        Head = head;
        TopLevel = topLevel;
        _digests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string path, string digest) in files) _digests.TryAdd(path, digest);

        Paths = files.Select(static file => file.Path)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A tree git could not describe, which no recorded verdict or baseline can match.</summary>
    public static TreeSnapshot Unknown { get; } = new(null, null, []);

    /// <summary>The commit HEAD names, or null when git could not say.</summary>
    public string? Head { get; }

    /// <summary>
    ///     The top level the paths are relative to, or null for a snapshot read back from the hook's state, which
    ///     keeps only what a comparison needs.
    /// </summary>
    public string? TopLevel { get; }

    /// <summary>The changed code files, relative to the repository root, as written, in git's order.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>Whether git described the tree.</summary>
    public bool Known => Head is not null;

    /// <summary>The digest recorded for <paramref name="path" />, or null when the snapshot does not name it.</summary>
    public string? DigestOf(string path)
    {
        return _digests.GetValueOrDefault(path);
    }

    /// <summary>
    ///     The snapshot of a tree whose HEAD and NUL-separated status listing git gave, kept to its code entries,
    ///     each digested by <paramref name="digestOf" /> from its root-relative path as the bytes are now.
    /// </summary>
    internal static TreeSnapshot Of(string head, string topLevel, string status, Func<string, string> digestOf)
    {
        List<string> kept = GitChangedFiles.ParseZTerminated(status)
            .Where(HookCodeFiles.IsCode)
            .Select(GitChangedFiles.StatusPath)
            .ToList();

        // A POSIX name may hold a line break, which a record of one file per line cannot carry, and an unknown tree
        // is checked.
        if (kept.Any(static path => path.Contains('\n'))) return Unknown;

        List<(string, string)> files = kept.Select(path => (path, digestOf(path)))
            .ToList();
        return new TreeSnapshot(head, topLevel, files);
    }

    /// <summary>A snapshot read back from the hook's state: HEAD and each file with its recorded digest.</summary>
    internal static TreeSnapshot Recorded(string head, IReadOnlyList<(string Path, string Digest)> files)
    {
        return new TreeSnapshot(head, null, files);
    }

    /// <summary>
    ///     The digest of <paramref name="file" />'s bytes as lowercase hex SHA-256, <see cref="Absent" /> when there
    ///     is no such file, or <see cref="Unreadable" /> when it cannot be read.
    /// </summary>
    internal static string Digest(string file)
    {
        try
        {
            if (!File.Exists(file)) return Absent;

            using FileStream stream = File.OpenRead(file);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexStringLower(hash);
        }
        catch (Exception ex) when (HookState.IsFileSystemFailure(ex))
        {
            return Unreadable;
        }
    }
}

/// <summary>What counts as code to the hook: the extensions a check reads.</summary>
internal static class HookCodeFiles
{
    private static readonly string[] Extensions =
        [".cs", ".csproj", ".props", ".targets", ".sln", ".slnx", ".razor", ".cshtml"];

    /// <summary>
    ///     Whether <paramref name="pathOrEntry" /> names a code file: a path, or a porcelain status entry, ending in
    ///     one of the extensions in any case. Only the end counts, so <c>Probe.csx</c> is not one.
    /// </summary>
    public static bool IsCode(string pathOrEntry)
    {
        return Extensions.Any(extension => pathOrEntry.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }
}
