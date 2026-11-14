using System.Diagnostics;
using Shouldly;
using SlideRule.Roslyn.Hosting;
using Xunit;

namespace SlideRule.Tests.Roslyn;

public sealed class DotnetCommandTests
{
    [Fact]
    public void ApplyCleanSdkEnvironment_StripsEveryVariableNamingAnotherMsBuild_AndKeepsTheRest()
    {
        // Arrange: a child environment carrying each variable this process's registration may have set.
        string[] registration =
        [
            "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath", "VSINSTALLDIR", "VSCMD_VER", "VSToolsPath",
            "VisualStudioVersion"
        ];
        ProcessStartInfo startInfo = new("dotnet");
        foreach (string name in registration) startInfo.Environment[name] = "inherited";
        startInfo.Environment["SLIDERULE_TEST_UNRELATED"] = "kept";

        // Act
        DotnetCommand.ApplyCleanSdkEnvironment(startInfo);

        // Assert
        startInfo.Environment.Keys
            .Intersect(registration, StringComparer.OrdinalIgnoreCase)
            .ShouldBeEmpty();
        startInfo.Environment["SLIDERULE_TEST_UNRELATED"]
            .ShouldBe("kept");
    }
}
