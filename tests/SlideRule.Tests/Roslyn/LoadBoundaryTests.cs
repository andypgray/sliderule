using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The load boundary over an <see cref="AdhocWorkspace" /> and a real solution file: the MSBuild load's removal of
///     the projects a solution's default configuration does not build, and <see cref="LoadBoundary.Settle" />.
///     <c>Cli/SolutionEntriesE2ETests</c> drives both through a real load and a real replay. This class pins that a
///     project a built one references leaves as well, since a solution build neither builds it nor passes its
///     assembly to the project that references it. It also pins that <see cref="LoadBoundary.Settle" /> keeps a
///     not-built project the solution still holds, as a replay's solution holds one its binlog compiled.
/// </summary>
public sealed class LoadBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithoutUnbuilt_NotBuiltProject_IsRemovedAndNamedAsNotBuiltWhetherABuiltOneReferencesItOrNot(bool referenced)
    {
        // Arrange — Skipped, which the default configuration does not build, with Built referencing it or not.
        using TempDirectory temp = TestTempRoot.Fresh("boundary-not-built");
        string solutionPath = WriteSolution(temp);
        ProjectInfo skipped = AdhocSolution.Loaded("Skipped", temp.PathOf("Skipped", "Skipped.csproj"));
        ProjectInfo built = AdhocSolution.Loaded("Built", temp.PathOf("Built", "Built.csproj"));
        if (referenced) built = built.WithProjectReferences([new ProjectReference(skipped.Id)]);
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(skipped, built);
        SolutionMembership? membership = LoadBoundary.ReadMembership(solutionPath);

        // Act — the MSBuild load's sequence.
        SettledSolution settled = LoadBoundary.Settle(LoadBoundary.WithoutUnbuilt(solution, membership), membership, []);

        // Assert
        settled.Solution.Projects
            .Select(project => project.Name)
            .ShouldBe(["Built"]);
        settled.Report.Unsupported.ShouldBe([
            new UnsupportedProject(temp.PathOf("Skipped", "Skipped.csproj"), UnsupportedProjectKind.NotBuilt)
        ]);
    }

    [Fact]
    public void Settle_NotBuiltProjectStillHeld_IsKeptAndNotListed()
    {
        // Arrange — the replay shape: a binlog of another configuration compiled Skipped, so the solution holds it.
        using TempDirectory temp = TestTempRoot.Fresh("boundary-replay-kept");
        string solutionPath = WriteSolution(temp);
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(
            AdhocSolution.Loaded("Skipped", temp.PathOf("Skipped", "Skipped.csproj")),
            AdhocSolution.Loaded("Built", temp.PathOf("Built", "Built.csproj")));

        // Act
        SettledSolution settled = LoadBoundary.Settle(solution, LoadBoundary.ReadMembership(solutionPath), []);

        // Assert
        settled.Solution.Projects
            .Select(project => project.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(["Built", "Skipped"]);
        settled.Report.Unsupported.ShouldBeEmpty();
    }

    // Built is built by the default configuration; Skipped is mapped there with no Build.0 line.
    private static string WriteSolution(TempDirectory temp)
    {
        return temp.WriteFile(
            ["Solution.sln"],
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Built", "Built\Built.csproj", "{A1A1A1A1-1111-1111-1111-111111111111}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Skipped", "Skipped\Skipped.csproj", "{B2B2B2B2-2222-2222-2222-222222222222}"
            EndProject
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		Debug|Any CPU = Debug|Any CPU
            	EndGlobalSection
            	GlobalSection(ProjectConfigurationPlatforms) = postSolution
            		{A1A1A1A1-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{A1A1A1A1-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU
            		{B2B2B2B2-2222-2222-2222-222222222222}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            	EndGlobalSection
            EndGlobal
            """);
    }
}
