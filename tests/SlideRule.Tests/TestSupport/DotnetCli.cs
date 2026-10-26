using System.Diagnostics;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Runs a <c>dotnet</c> CLI command in a clean SDK environment, draining and bounding it through
///     <see cref="ChildProcess" /> and throwing with captured output on a non-zero exit.
/// </summary>
/// <remarks>
///     The environment hygiene is exposed separately as <see cref="ApplyCleanSdkEnvironment" />, so a test
///     that shells the SDK itself can shape the same deployment-normal environment from the one
///     poison-var list rather than assembling its own.
/// </remarks>
internal static class DotnetCli
{
    /// <summary>
    ///     Runs <c>dotnet <paramref name="arguments" /> --disable-build-servers</c> in
    ///     <paramref name="workingDirectory" />, throwing <see cref="InvalidOperationException" /> (carrying
    ///     stdout + stderr) on a non-zero exit. Every command it runs builds or restores, and the switch keeps the
    ///     command from leaving a compiler or Razor server running after it exits.
    /// </summary>
    internal static void Run(string arguments, string workingDirectory)
    {
        var commandLine = $"{arguments} --disable-build-servers";
        ProcessStartInfo startInfo = new("dotnet", commandLine)
        {
            WorkingDirectory = workingDirectory
        };
        ApplyCleanSdkEnvironment(startInfo);

        ChildProcess.ProcessResult result = ChildProcess.Run(startInfo);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet {commandLine}' failed with exit code {result.ExitCode}."
                + $"{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
    }

    /// <summary>
    ///     Gives <paramref name="startInfo" /> a clean SDK environment: node-reuse + MSBuild-server off, and
    ///     the test host's Visual Studio / MSBuild registration env vars stripped.
    /// </summary>
    /// <remarks>
    ///     Node-reuse + MSBuild-server off: otherwise a reused worker node (or, on newer SDKs, an MSBuild
    ///     server) lingers after the command exits, inherits the child's redirected stdout write-handle, and
    ///     the pipe never reaches EOF — the <see cref="ChildProcess" /> drain would then unblock only once
    ///     its ceiling elapsed and it killed the tree. The
    ///     stripped vars matter because the test process has Visual Studio's MSBuild registered (MSBuildLocator
    ///     + <c>MsBuildBootstrap</c> set MSBUILD_EXE_PATH / VSINSTALLDIR / VSCMD_VER process-wide for the
    ///     Roslyn BuildHost); a child inheriting those resolves the wrong MSBuild and fails immediately.
    /// </remarks>
    internal static void ApplyCleanSdkEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";

        foreach (string poisonedVariable in new[]
                 {
                     "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath",
                     "VSINSTALLDIR", "VSCMD_VER", "VisualStudioVersion"
                 })
            startInfo.Environment.Remove(poisonedVariable);
    }
}
