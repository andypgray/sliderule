using System.ComponentModel;
using SlideRule.Checking;
using SlideRule.Internal;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Diff;

/// <summary>
///     Resolves the files changed since a git ref into a <see cref="DiffContext" /> for the scope
///     tripwires (GRAMMAR §7).
/// </summary>
/// <remarks>
///     <para>
///         Two listings are joined, the paths committed since the ref and the status against HEAD, untracked
///         files included, so a brand-new file an agent writes into dragon territory trips its scope.
///     </para>
///     <para>
///         Neither listing compares the worktree with the ref directly. That diff refreshes the index and
///         writes it back under <c>index.lock</c> whatever <c>GIT_OPTIONAL_LOCKS</c> says, so a commit made while a
///         hook checks fails; a diff of two trees never reads the worktree, and the status refreshes in memory
///         only. The listing is therefore what was committed since the ref together with what changed against
///         HEAD, so a file changed and then changed back is listed, which for a tripwire that only warns is the
///         safe direction.
///     </para>
///     <para>
///         Every failure is loud: git missing on PATH, not a repository, a bad ref, or a timeout all throw
///         <see cref="UserErrorException" /> (exit 2).
///     </para>
/// </remarks>
internal static class GitChangedFiles
{
    /// <summary>
    ///     Runs git and returns the files committed since <paramref name="baseRef" /> together with those
    ///     staged, unstaged or untracked, as a <see cref="DiffContext" /> (absolute, forward-slash paths).
    /// </summary>
    public static async Task<DiffContext> ResolveAsync(string baseRef, string solutionDirectory, CancellationToken ct)
    {
        // The three commands share nothing and process spawn dominates, so they run together. They are
        // awaited in launch order because that order is the refusal precedence: a directory that is not a
        // repository still surfaces rev-parse's message, not whichever child failed first.
        Task<string> toplevelRun = RunGitAsync(solutionDirectory, ct, "rev-parse", "--show-toplevel");
        Task<string> committedRun = RunGitAsync(solutionDirectory, ct, CommittedArguments(baseRef, "HEAD"));
        Task<string> statusRun = RunGitAsync(solutionDirectory, ct, StatusArguments);

        string toplevelOutput = await toplevelRun;
        string toplevel = Path.GetFullPath(toplevelOutput.Trim());

        string committedOutput = await committedRun;
        string statusOutput = await statusRun;

        IReadOnlyList<string> committed = ParseZTerminated(committedOutput);
        IEnumerable<string> uncommitted = StatusPaths(statusOutput);

        IReadOnlyList<string> files = ComposeAbsolute(toplevel, committed.Concat(uncommitted));
        return new DiffContext(solutionDirectory, files);
    }

    /// <summary>Splits git's NUL-terminated output into non-empty entries (tolerates a missing trailing NUL).</summary>
    internal static IReadOnlyList<string> ParseZTerminated(string output)
    {
        return output.Split('\0').Where(entry => entry.Length > 0).ToList();
    }

    /// <summary>
    ///     The <c>git status</c> arguments both changed-file listings are read with, this one and the hook's snapshot.
    ///     <see cref="StatusPaths" /> slices an entry correctly only under exactly these flags.
    /// </summary>
    internal static readonly string[] StatusArguments = ["status", "--porcelain=v1", "-uall", "-z", "--no-renames"];

    /// <summary>
    ///     The <c>git diff</c> arguments that list the paths differing between commits <paramref name="from" /> and
    ///     <paramref name="to" />, for this listing and the hook's alike. With renames off a rename names both of its
    ///     paths, so a file moved out of a directory is listed at the path it left.
    /// </summary>
    internal static string[] CommittedArguments(string from, string to)
    {
        return ["diff", "--name-only", "-z", "--no-renames", from, to, "--"];
    }

    /// <summary>
    ///     The paths a <c>git status</c> listing read with <see cref="StatusArguments" /> names, relative to the
    ///     repository root, in git's order.
    /// </summary>
    internal static IEnumerable<string> StatusPaths(string statusOutput)
    {
        return ParseZTerminated(statusOutput).Select(StatusPath);
    }

    // Everything after the two status letters and the space that follows them.
    private static string StatusPath(string entry)
    {
        return entry[3..];
    }

    /// <summary>
    ///     Rebases toplevel-relative paths onto absolute, forward-slash paths, deduped per-OS
    ///     (<see cref="PathComparison" />) in first-seen order — so two case-variant changed files stay
    ///     distinct on a case-sensitive file system rather than one silently swallowing the other.
    /// </summary>
    internal static IReadOnlyList<string> ComposeAbsolute(string toplevel, IEnumerable<string> relative)
    {
        var seen = new HashSet<string>(PathComparison.Comparer);
        var result = new List<string>();
        foreach (string rel in relative)
        {
            string absolute = Path.GetFullPath(Path.Combine(toplevel, rel)).Replace('\\', '/');
            if (seen.Add(absolute)) result.Add(absolute);
        }

        return result;
    }

    private static async Task<string> RunGitAsync(
        string solutionDirectory, CancellationToken ct, params string[] arguments)
    {
        ChildProcess.ProcessResult result;
        try
        {
            result = await GitProcess.RunAsync(solutionDirectory, ct, arguments);
        }
        catch (Win32Exception ex)
        {
            // git is not on PATH (or is not executable).
            throw Failure($"git could not be run ({ex.Message}); --diff-base needs git on PATH.");
        }
        catch (InvalidOperationException)
        {
            throw Failure("git could not be started.");
        }
        catch (TimeoutException)
        {
            throw Failure(
                $"'git {string.Join(" ", arguments)}' did not complete within {GitProcess.TimeoutSeconds} seconds.");
        }

        if (result.ExitCode != 0)
        {
            string err = result.StandardError.Trim();
            string suffix = err.Length > 0 ? $": {err}" : ".";
            throw Failure($"'git {string.Join(" ", arguments)}' exited with code {result.ExitCode}{suffix}");
        }

        return result.StandardOutput;
    }

    private static UserErrorException Failure(string detail)
    {
        return new UserErrorException($"--diff-base could not resolve changed files: {detail}");
    }
}
