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
        ChildProcess.ProcessResult result = Start(commandLine, workingDirectory);
        if (result.ExitCode != 0) throw result.FailureOf($"dotnet {commandLine}");
    }

    /// <summary>
    ///     <see cref="Run" /> for a command that must fail, such as a build whose compile is meant to be refused;
    ///     throws <see cref="InvalidOperationException" /> when it exits zero.
    /// </summary>
    internal static void RunExpectingFailure(string arguments, string workingDirectory)
    {
        var commandLine = $"{arguments} --disable-build-servers";
        ChildProcess.ProcessResult result = Start(commandLine, workingDirectory);
        if (result.ExitCode == 0)
            throw new InvalidOperationException($"dotnet {commandLine} succeeded, and was expected to fail.");
    }

    private static ChildProcess.ProcessResult Start(string commandLine, string workingDirectory)
    {
        ProcessStartInfo startInfo = new("dotnet", commandLine)
        {
            WorkingDirectory = workingDirectory
        };
        DotnetCommand.ApplyCleanSdkEnvironment(startInfo);

        return ChildProcess.Run(startInfo);
    }
}
