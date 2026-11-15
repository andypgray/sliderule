using System.Diagnostics;
using Shouldly;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Ci;

/// <summary>
///     Runs the committed <c>.github/scripts/check-sdk.sh</c> as a real child process against a stub <c>dotnet</c>,
///     and holds it to what every CI job relies on after setup-dotnet: it passes only when the SDK in use is in the
///     first band the job names and every SDK listed is in a band it names and under <c>DOTNET_INSTALL_DIR</c>, and a
///     refusal names the SDK it found.
/// </summary>
public sealed class SdkCheckScriptTests
{
    [Fact]
    public void TheAskedBandAlone_PassesAndNamesTheSdkInUse()
    {
        using TempDirectory temp = TestTempRoot.Fresh("check-sdk");
        string stub = Stub(temp, "10.0.404", "10.0.404 [/job/sdk]\n");

        ChildProcess.ProcessResult result = CheckSdk(stub, "10.0");

        result.ExitCode.ShouldBe(0, result.Transcript());
        result.StandardOutput.ShouldBe($"SDK 10.0.404 from {ShouldReadShellSpelling(stub)}/dotnet\n");
        result.StandardError.ShouldBeEmpty();
    }

    [Fact]
    public void AnSdkOutsideTheBandInUse_FailsNamingIt()
    {
        using TempDirectory temp = TestTempRoot.Fresh("check-sdk");
        string stub = Stub(temp, "11.0.100", "10.0.404 [/job/sdk]\n11.0.100 [/job/sdk]\n");

        ChildProcess.ProcessResult result = CheckSdk(stub, "10.0");

        result.ExitCode.ShouldBe(1, result.Transcript());
        result.StandardError.ShouldBe("::error::This job asks for SDK 10.0 but runs 11.0.100.\n");
    }

    [Fact]
    public void AnotherBandListedBesideTheOneInUse_FailsNamingIt()
    {
        // CRLF, as dotnet writes it on Windows, where the steps run this under Git Bash.
        using TempDirectory temp = TestTempRoot.Fresh("check-sdk");
        string stub = Stub(temp, "10.0.404", "10.0.404 [/job/sdk]\r\n11.0.100 [/other]\r\n9.0.321 [/other]\r\n");

        ChildProcess.ProcessResult result = CheckSdk(stub, "10.0");

        result.ExitCode.ShouldBe(1, result.Transcript());
        result.StandardError
            .ShouldBe("::error::This job asks for SDK 10.0 only, and dotnet also lists: 11.0.100 [/other]; 9.0.321 [/other]\n");
    }

    [Fact]
    public void ABandThatIsNotMajorDotMinor_ExitsTwoNamingIt()
    {
        using TempDirectory temp = TestTempRoot.Fresh("check-sdk");
        string stub = Stub(temp, "10.0.404", "10.0.404 [/job/sdk]\n");

        ChildProcess.ProcessResult result = CheckSdk(stub, "10.0.x");

        result.ExitCode.ShouldBe(2, result.Transcript());
        result.StandardError.ShouldBe("::error::check-sdk.sh takes one SDK band, such as 10.0, not '10.0.x'.\n");
    }

    [Fact]
    public void TheBandInUseWithAnotherAskedBesideIt_PassesAndNamesTheSdkInUse()
    {
        using TempDirectory temp = TestTempRoot.Fresh("check-sdk");
        string stub = Stub(temp, "11.0.100", "10.0.404 [/job/sdk]\n11.0.100 [/job/sdk]\n");

        ChildProcess.ProcessResult result = CheckSdk(stub, "11.0", "10.0");

        result.ExitCode.ShouldBe(0, result.Transcript());
        result.StandardOutput.ShouldBe($"SDK 11.0.100 from {ShouldReadShellSpelling(stub)}/dotnet\n");
        result.StandardError.ShouldBeEmpty();
    }

    [Fact]
    public void TheBandBesideInUse_FailsNamingTheSdkInUse()
    {
        using TempDirectory temp = TestTempRoot.Fresh("check-sdk");
        string stub = Stub(temp, "10.0.404", "10.0.404 [/job/sdk]\n11.0.100 [/job/sdk]\n");

        ChildProcess.ProcessResult result = CheckSdk(stub, "11.0", "10.0");

        result.ExitCode.ShouldBe(1, result.Transcript());
        result.StandardError.ShouldBe("::error::This job asks for SDK 11.0 but runs 10.0.404.\n");
    }

    [Fact]
    public void AThirdBandListedBesideTheTwoAskedFor_FailsNamingIt()
    {
        using TempDirectory temp = TestTempRoot.Fresh("check-sdk");
        string stub = Stub(temp, "11.0.100", "9.0.321 [/other]\n10.0.404 [/job/sdk]\n11.0.100 [/job/sdk]\n");

        ChildProcess.ProcessResult result = CheckSdk(stub, "11.0", "10.0");

        result.ExitCode.ShouldBe(1, result.Transcript());
        result.StandardError
            .ShouldBe("::error::This job asks for SDK 11.0, and 10.0 beside it, and dotnet also lists: 9.0.321 [/other]\n");
    }

    [Fact]
    public void ABandBesideThatIsNotMajorDotMinor_ExitsTwoNamingIt()
    {
        using TempDirectory temp = TestTempRoot.Fresh("check-sdk");
        string stub = Stub(temp, "11.0.100", "10.0.404 [/job/sdk]\n11.0.100 [/job/sdk]\n");

        ChildProcess.ProcessResult result = CheckSdk(stub, "11.0", "10");

        result.ExitCode.ShouldBe(2, result.Transcript());
        result.StandardError.ShouldBe("::error::check-sdk.sh takes SDK bands such as 10.0, and '10' is not one.\n");
    }

    [Fact]
    public void NoInstallDirectory_ExitsTwoNamingTheVariable()
    {
        using TempDirectory temp = TestTempRoot.Fresh("check-sdk");
        string stub = Stub(temp, "10.0.404", "10.0.404 [/job/sdk]\n");

        ChildProcess.ProcessResult result = CheckSdkInstalledIn(stub, installDirectory: null, "10.0");

        result.ExitCode.ShouldBe(2, result.Transcript());
        result.StandardError.ShouldBe(
            "::error::check-sdk.sh needs DOTNET_INSTALL_DIR, the directory setup-dotnet was told to install into.\n");
    }

    [Fact]
    public void AnSdkOfTheBandOutsideTheInstallDirectory_FailsNamingIt()
    {
        using TempDirectory temp = TestTempRoot.Fresh("check-sdk");
        string stub = Stub(
            temp, "10.0.404", "10.0.404 [/job/sdk]\n10.0.100 [/usr/share/dotnet/sdk]\n10.0.200 [/opt/dotnet/sdk]\n");

        ChildProcess.ProcessResult result = CheckSdk(stub, "10.0");

        result.ExitCode.ShouldBe(1, result.Transcript());
        result.StandardError.ShouldBe(
            "::error::This job installs into /job, and dotnet lists SDKs outside it: "
            + "10.0.100 [/usr/share/dotnet/sdk]; 10.0.200 [/opt/dotnet/sdk]\n");
    }

    [Fact]
    public void TheWindowsSpellingsOfTheInstallDirectory_Pass()
    {
        // RUNNER_TEMP's backslashes with /.dotnet appended, against dotnet's own spelling, the drive letters cased
        // apart.
        using TempDirectory temp = TestTempRoot.Fresh("check-sdk");
        string stub = Stub(temp, "10.0.404", @"10.0.404 [D:\a\_temp\.dotnet\sdk]" + "\r\n");

        ChildProcess.ProcessResult result = CheckSdkInstalledIn(stub, @"d:\a\_temp/.dotnet", "10.0");

        result.ExitCode.ShouldBe(0, result.Transcript());
        result.StandardError.ShouldBeEmpty();
    }

    /// <summary>A stub <c>dotnet</c> answering <c>--version</c> and <c>--list-sdks</c>, in its own directory.</summary>
    private static string Stub(TempDirectory temp, string version, string listSdks)
    {
        string directory = temp.PathOf("stub");
        Directory.CreateDirectory(directory);
        string listed = listSdks.Replace("\\", "\\\\")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
        ShellInterpreter.WriteExecutableStub(
            directory,
            "dotnet",
            $"#!/bin/sh\ncase \"$1\" in\n  --version) printf '{version}\\n' ;;\n  --list-sdks) printf '{listed}' ;;\nesac\n");

        return directory;
    }

    /// <summary>
    ///     <paramref name="directory" /> as bash spells it, which is <c>/c/...</c> under Git Bash and the path itself
    ///     elsewhere; fails the test when bash cannot enter it.
    /// </summary>
    private static string ShouldReadShellSpelling(string directory)
    {
        var startInfo = new ProcessStartInfo(ShellInterpreter.Require()) { WorkingDirectory = directory };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("pwd");

        ChildProcess.ProcessResult result = ChildProcess.Run(startInfo, TimeSpan.FromMinutes(1));
        result.ExitCode.ShouldBe(0, result.Transcript());

        return result.StandardOutput.TrimEnd('\n');
    }

    /// <summary>
    ///     Runs the committed script with <paramref name="bands" /> in a job that installs into <c>/job</c>, under
    ///     bash as every CI step runs it.
    /// </summary>
    private static ChildProcess.ProcessResult CheckSdk(string stubDirectory, params string[] bands)
    {
        return CheckSdkInstalledIn(stubDirectory, "/job", bands);
    }

    /// <summary>Runs the committed script with <c>DOTNET_INSTALL_DIR</c> unset when the directory is null.</summary>
    private static ChildProcess.ProcessResult CheckSdkInstalledIn(
        string stubDirectory, string? installDirectory, params string[] bands)
    {
        ProcessStartInfo startInfo = ShellInterpreter.CommittedScript(
            ".github/scripts/check-sdk.sh", stubDirectory, stubDirectory, bands);
        if (installDirectory is null) startInfo.Environment.Remove("DOTNET_INSTALL_DIR");
        else startInfo.Environment["DOTNET_INSTALL_DIR"] = installDirectory;

        return ChildProcess.Run(startInfo, TimeSpan.FromMinutes(1));
    }
}
