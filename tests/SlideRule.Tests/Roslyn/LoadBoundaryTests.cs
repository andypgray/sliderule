using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="LoadBoundary.Settle" />'s removal of the projects a solution's default configuration does not
///     build, over an <see cref="AdhocWorkspace" /> and a real solution file. <c>Cli/SolutionEntriesE2ETests</c>
///     drives the removal through a real load; what is pinned here is that a project a built one references leaves
///     as well, since a solution build neither builds it nor passes its assembly to the project that references it.
/// </summary>
public sealed class LoadBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Settle_NotBuiltProject_IsRemovedAndNamedAsNotBuiltWhetherABuiltOneReferencesItOrNot(bool referenced)
    {
        // Arrange — Skipped, which the default configuration does not build, with Built referencing it or not.
        using TempDirectory temp = TestTempRoot.Fresh("boundary-not-built");
        string solutionPath = WriteSolution(temp);
        ProjectInfo skipped = AdhocSolution.Loaded("Skipped", temp.PathOf("Skipped", "Skipped.csproj"));
        ProjectInfo built = AdhocSolution.Loaded("Built", temp.PathOf("Built", "Built.csproj"));
        if (referenced) built = built.WithProjectReferences([new ProjectReference(skipped.Id)]);
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(skipped, built);

        // Act
        SettledSolution settled = LoadBoundary.Settle(solution, solutionPath);

        // Assert
        settled.Solution.Projects
            .Select(project => project.Name)
            .ShouldBe(["Built"]);
        settled.Report.Unsupported.ShouldBe([
            new UnsupportedProject(temp.PathOf("Skipped", "Skipped.csproj"), UnsupportedProjectKind.NotBuilt)
        ]);
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
