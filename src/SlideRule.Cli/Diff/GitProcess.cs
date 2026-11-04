using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Diff;

/// <summary>
///     How this tool starts git: rooted at a directory with <c>git -C</c>, through <see cref="ChildProcess" />,
///     under one bound, with git's optional locks off. <see cref="RunAsync" /> leaves what a failure means to each
///     caller; <see cref="AnswerAsync" /> reads every failure as no answer.
/// </summary>
internal static class GitProcess
{
    // Generous for a read-only query and still far inside the hook's own timeout, so a wedged git costs a stop its
    // check rather than the whole budget Claude Code gives the hook.
    internal const int TimeoutSeconds = 30;

    // Every launch goes through ChildProcess, whose closed stdin is what keeps git from wedging. Without it the
    // child inherits this process's stdin, and Git for Windows' startup handle probe blocks forever against an
    // inherited live pipe. Both hosts hold one: inside the MCP server, the client's JSON-RPC pipe parked on a
    // synchronous read, and under the hook, the pipe Claude Code wrote the event payload into. The bound is a
    // safety net behind the closed stdin, linked with the caller's token so a cancel aborts the wait instead of
    // leaving a zombie to run the clock out. Launch failures propagate for the caller to map: Win32Exception (git
    // is not on PATH, or the directory is missing), InvalidOperationException, and TimeoutException.
    internal static Task<ChildProcess.ProcessResult> RunAsync(
        string directory, CancellationToken ct, params string[] arguments)
    {
        // Resolved once for both roots: a relative directory given to each would be applied twice, the child starting
        // in it and -C then stepping into it again. Against the current directory, where an empty one means here.
        string root = Path.GetFullPath(directory, Directory.GetCurrentDirectory());
        var startInfo = new ProcessStartInfo("git")
        {
            CreateNoWindow = true,
            WorkingDirectory = root,
            // git writes paths as UTF-8, and left unset .NET decodes a child's output with the console's code page on
            // Windows, which turns a non-ASCII name into one that names no file.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        // Every git this tool starts is a read made beside someone else's work, so each is asked to take no lock it
        // can do without: a git status would otherwise rewrite the index at every prompt and stop, and a commit made
        // at that moment fails on index.lock. The variable rather than the option, because a git that predates both
        // ignores an unknown variable but refuses an unknown option.
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        // -C roots git at the directory regardless of the host process's cwd.
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(root);
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);

        TimeSpan bound = TimeSpan.FromSeconds(TimeoutSeconds);
        return ChildProcess.RunAsync(startInfo, bound, ct: ct);
    }

    /// <summary>
    ///     git's standard output when it exits 0, or null when it fails, is not on PATH, cannot start in the
    ///     directory, or outlives its bound. A cancel still propagates.
    /// </summary>
    // The exit code decides, not the output: rev-parse HEAD in a repository with no commit prints "HEAD" and fails.
    internal static async Task<string?> AnswerAsync(string directory, CancellationToken ct, params string[] arguments)
    {
        try
        {
            ChildProcess.ProcessResult result = await RunAsync(directory, ct, arguments);
            return result.ExitCode == 0 ? result.StandardOutput : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    ///     <see cref="AnswerAsync" />'s answer read as <see cref="AnswerLines" />, or null where it gave none.
    /// </summary>
    internal static async Task<string[]?> AnswerLinesAsync(
        string directory, CancellationToken ct, params string[] arguments)
    {
        string? output = await AnswerAsync(directory, ct, arguments);
        return output is null ? null : AnswerLines(output);
    }

    /// <summary>
    ///     git's line-per-answer output split into its lines, each with its line ending cut and nothing else, so an
    ///     empty answer, such as <c>--show-prefix</c> at the top level, is an empty line.
    /// </summary>
    // Only the line ending is cut: a folder's own name can start or end with a space.
    internal static string[] AnswerLines(string output)
    {
        string[] lines = output.Split('\n');
        int count = output.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        return lines
            .Take(count)
            .Select(static line => line.TrimEnd('\r'))
            .ToArray();
    }
}
