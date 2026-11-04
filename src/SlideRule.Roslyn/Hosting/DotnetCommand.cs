using System.Diagnostics;

namespace SlideRule.Roslyn.Hosting;

/// <summary>
///     A <c>dotnet</c> command line ready for <see cref="ChildProcess" />, in the SDK environment a deployment would
///     give it rather than the one this process carries.
/// </summary>
internal static class DotnetCommand
{
    // MsBuildBootstrap registers Visual Studio's MSBuild by setting these process-wide, and a child that inherits
    // them resolves that MSBuild instead of its own SDK's and fails at once.
    private static readonly string[] PoisonedVariables =
    [
        "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath", "VSINSTALLDIR", "VSCMD_VER",
        "VisualStudioVersion"
    ];

    /// <summary>
    ///     The start info for <c>dotnet</c> with <paramref name="arguments" />, each passed as one argument, run in
    ///     <paramref name="workingDirectory" /> under <see cref="ApplyCleanSdkEnvironment" />.
    /// </summary>
    internal static ProcessStartInfo StartInfo(IEnumerable<string> arguments, string workingDirectory)
    {
        ProcessStartInfo startInfo = new("dotnet") { WorkingDirectory = workingDirectory };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);

        ApplyCleanSdkEnvironment(startInfo);
        return startInfo;
    }

    /// <summary>
    ///     Turns node reuse and the MSBuild server off for the child, and strips the MSBuild registration variables
    ///     this process may carry.
    /// </summary>
    /// <remarks>
    ///     A reused worker node or an MSBuild server outlives the command, inherits its redirected stdout, and keeps
    ///     the pipe from reaching EOF, so <see cref="ChildProcess" /> would wait out its ceiling and kill the tree.
    /// </remarks>
    internal static void ApplyCleanSdkEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";

        foreach (string poisonedVariable in PoisonedVariables) startInfo.Environment.Remove(poisonedVariable);
    }
}
