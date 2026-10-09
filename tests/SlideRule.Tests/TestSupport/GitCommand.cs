using System.Diagnostics;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Runs <c>git</c> against a directory for the suites that need a real repository underneath them —
///     a committed fixture to diff against, or a linked worktree to be fired at.
/// </summary>
/// <remarks>
///     Launched through <see cref="ChildProcess" />, so this git gets the closed stdin, the bounded wait
///     and the kill-tree every child in this repository gets. A non-zero exit throws with git's own two
///     output channels in the message, because the first question a failed setup raises is what git said —
///     except through <see cref="Attempt" />, whose callers are asking about the code itself.
/// </remarks>
internal static class GitCommand
{
    /// <summary>
    ///     Runs <c>git -C <paramref name="workingDirectory" /> <paramref name="args" /></c>, throwing on a
    ///     non-zero exit.
    /// </summary>
    internal static void Run(string workingDirectory, params string[] args)
    {
        Execute(workingDirectory, args);
    }

    /// <summary>
    ///     The same, returning git's standard output — line-oriented and LF-normalized, as
    ///     <see cref="ChildProcess.Run" /> captures it.
    /// </summary>
    internal static string Output(string workingDirectory, params string[] args)
    {
        return Execute(workingDirectory, args)
            .StandardOutput;
    }

    /// <summary>
    ///     The same, returning git's whole result rather than throwing on a non-zero exit — for the callers
    ///     whose subject <em>is</em> the exit code. <c>git merge-file</c> reports the number of conflicts
    ///     that way, so a clean merge and a conflicted one are both successful runs of the command.
    /// </summary>
    internal static ChildProcess.ProcessResult Attempt(string workingDirectory, params string[] args)
    {
        return Execute(workingDirectory, args, throwOnFailure: false);
    }

    /// <summary>
    ///     Runs <c>git commit <paramref name="args" /></c> in <paramref name="directory" /> with the identity and the
    ///     signing setting riding on the command, so the commit succeeds whatever the host's global git config
    ///     says — or does not say.
    /// </summary>
    /// <remarks>
    ///     On the command rather than written into the repository: both are properties of the commit being made,
    ///     and a repository that never commits needs neither.
    /// </remarks>
    internal static void Commit(string directory, params string[] args)
    {
        Run(
            directory,
            [
                "-c", "user.email=sliderule-test@example.invalid",
                "-c", "user.name=SlideRule Test",
                "-c", "commit.gpgsign=false",
                "commit", .. args
            ]);
    }

    private static ChildProcess.ProcessResult Execute(
        string workingDirectory, string[] args, bool throwOnFailure = true)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(workingDirectory);
        foreach (string argument in args) startInfo.ArgumentList.Add(argument);

        ChildProcess.ProcessResult result = ChildProcess.Run(startInfo);
        if (throwOnFailure && result.ExitCode != 0)
            throw new InvalidOperationException(
                $"'git {string.Join(" ", args)}' failed with exit code {result.ExitCode}."
                + $"{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");

        return result;
    }
}
