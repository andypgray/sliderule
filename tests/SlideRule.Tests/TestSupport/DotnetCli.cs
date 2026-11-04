using System.Diagnostics;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Runs a <c>dotnet</c> CLI command in the clean SDK environment <see cref="DotnetCommand" /> gives production
///     children, draining and bounding it through <see cref="ChildProcess" /> and throwing with captured output on a
///     non-zero exit.
/// </summary>
internal static class DotnetCli
{
    /// <summary>
    ///     Runs <c>dotnet <paramref name="arguments" /> --disable-build-servers</c> in
    ///     <paramref name="workingDirectory" />, throwing <see cref="InvalidOperationException" /> (carrying
    ///     stdout + stderr) on a non-zero exit. The switch keeps the command from leaving a compiler or Razor
    ///     server running after it exits. It is appended to every command, so <paramref name="arguments" /> must
    ///     name one that accepts it, as build and restore do.
    /// </summary>
    internal static void Run(string arguments, string workingDirectory)
    {
        var commandLine = $"{arguments} --disable-build-servers";
        ProcessStartInfo startInfo = new("dotnet", commandLine)
        {
            WorkingDirectory = workingDirectory
        };
        DotnetCommand.ApplyCleanSdkEnvironment(startInfo);

        ChildProcess.ProcessResult result = ChildProcess.Run(startInfo);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet {commandLine}' failed with exit code {result.ExitCode}."
                + $"{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
    }
}
