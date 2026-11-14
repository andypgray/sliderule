using SlideRule.Roslyn.Hosting;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Readers of a finished child process's result, for a red's message and a failed setup step's exception.
/// </summary>
internal static class ProcessResultReads
{
    /// <summary>The exit code and both output channels, labelled, for a red's message.</summary>
    internal static string Transcript(this ChildProcess.ProcessResult result)
    {
        return $"exit {result.ExitCode}\nstdout:\n{result.StandardOutput}\nstderr:\n{result.StandardError}";
    }

    /// <summary>
    ///     The exception a setup step throws when <paramref name="command" /> exited non-zero, carrying both
    ///     channels.
    /// </summary>
    internal static InvalidOperationException FailureOf(this ChildProcess.ProcessResult result, string command)
    {
        return new InvalidOperationException(
            $"'{command}' failed with exit code {result.ExitCode}."
            + $"{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
    }
}
