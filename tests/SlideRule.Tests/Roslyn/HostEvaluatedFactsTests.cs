using Shouldly;
using SlideRule.Extraction;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The project facts are what an evaluation answers on the machine that runs it (GRAMMAR §4.10): a condition on
///     an environment variable or on the host's operating system resolves in the process evaluating the project,
///     so the same project file can declare a package on one machine and not on another.
/// </summary>
/// <remarks>
///     In the serial collection, because the environment row sets a process-wide variable for the length of one
///     evaluation, and a parallel test evaluating a project could read it.
/// </remarks>
[Collection("Serial")]
public sealed class HostEvaluatedFactsTests
{
    private const string Variable = "SLIDERULE_TEST_HOST_EVALUATED_FACT";

    [Fact]
    public void EvaluateAll_PackageConditionedOnAnEnvironmentVariable_FollowsTheEvaluatingProcess()
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("host-evaluated-environment");
        string projectFile = temp.WriteFile(["Bus", "Bus.csproj"], $"""
                                                                    <Project Sdk="Microsoft.NET.Sdk">
                                                                        <PropertyGroup>
                                                                            <TargetFramework>net10.0</TargetFramework>
                                                                        </PropertyGroup>
                                                                        <ItemGroup Condition="'$({Variable})' == 'true'">
                                                                            <PackageReference Include="Windows.Only" Version="1.0.0" />
                                                                        </ItemGroup>
                                                                    </Project>
                                                                    """);

        // Act
        IReadOnlyList<string> set;
        using (new ScopedEnvironmentVariable(Variable, "true"))
        {
            set = ShouldEvaluatePackageNames(projectFile);
        }

        IReadOnlyList<string> unset = ShouldEvaluatePackageNames(projectFile);

        // Assert
        set.ShouldBe(["Windows.Only"]);
        unset.ShouldBeEmpty();
    }

    [Fact]
    public void EvaluateAll_PackageConditionedOnTheHostOs_FollowsTheMachineThatEvaluates()
    {
        // Arrange — the shape MassTransit uses to add net472 and its packages on Windows alone.
        using TempDirectory temp = TestTempRoot.Fresh("host-evaluated-os");
        string projectFile = temp.WriteFile(["Bus", "Bus.csproj"], """
                                                                   <Project Sdk="Microsoft.NET.Sdk">
                                                                       <PropertyGroup>
                                                                           <TargetFramework>net10.0</TargetFramework>
                                                                       </PropertyGroup>
                                                                       <ItemGroup Condition="$([MSBuild]::IsOSPlatform('Windows'))">
                                                                           <PackageReference Include="Windows.Only" Version="1.0.0" />
                                                                       </ItemGroup>
                                                                   </Project>
                                                                   """);

        // Act
        IReadOnlyList<string> declared = ShouldEvaluatePackageNames(projectFile);

        // Assert — one verdict on a Windows leg and the other everywhere else, from one project file.
        declared.ShouldBe(OperatingSystem.IsWindows() ? ["Windows.Only"] : []);
    }

    private static IReadOnlyList<string> ShouldEvaluatePackageNames(string projectFile)
    {
        ProjectArtifactFacts facts = ProjectFactsEvaluator
            .EvaluateAll([new ProjectEvaluationRequest(projectFile, null)], solutionFile: null, Ct)
            .ShouldHaveSingleItem()
            .ShouldNotBeNull();

        return facts.PackageReferences.Select(package => package.Name)
            .ToList();
    }
}
