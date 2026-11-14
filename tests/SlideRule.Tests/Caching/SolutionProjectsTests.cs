using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Roslyn.Caching;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Caching;

/// <summary>
///     <see cref="SolutionProjects" />' collapse of a solution's <see cref="Project" />s onto one entry per project,
///     over solutions built in memory.
/// </summary>
public sealed class SolutionProjectsTests
{
    [Fact]
    public void CollectForeign_VisualBasicProjectLoadedForTwoFrameworks_UnionsTheirDocumentsUnderOneEntry()
    {
        // Arrange — one .vbproj loaded twice, as a multi-targeted project loads, each framework compiling a document
        // the other does not, and a C# project beside it that the foreign collection leaves out.
        using TempDirectory temp = TestTempRoot.Fresh("solution-projects");
        string vbproj = temp.PathOf("Legacy", "Legacy.vbproj");
        string shared = temp.PathOf("Legacy", "Ledger.vb");
        string firstOnly = temp.PathOf("Legacy", "Net48Only.vb");
        string secondOnly = temp.PathOf("Legacy", "Net10Only.vb");
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.CurrentSolution;
        solution = WithProject(solution, LanguageNames.VisualBasic, vbproj, shared, firstOnly);
        solution = WithProject(solution, LanguageNames.VisualBasic, vbproj, shared, secondOnly);
        solution = WithProject(solution, LanguageNames.CSharp, temp.PathOf("App", "App.csproj"), temp.PathOf("App", "Widget.cs"));

        // Act
        IReadOnlyList<ForeignProjectInputs> foreign = SolutionProjects.CollectForeign(solution, static (_, _) => true);

        // Assert
        ForeignProjectInputs legacy = foreign.ShouldHaveSingleItem();
        legacy.ShouldSatisfyAllConditions(
            () => legacy.ProjectFilePath.ShouldBe(vbproj),
            () => legacy.SourceSuffix.ShouldBe(".vb"),
            () => legacy.DocumentPaths.ShouldBe([shared, secondOnly, firstOnly]));
    }

    private static Solution WithProject(Solution solution, string language, string projectFile, params string[] documents)
    {
        var projectId = ProjectId.CreateNewId();
        string name = Path.GetFileNameWithoutExtension(projectFile);
        Solution withProject = solution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Default, name, name, language, filePath: projectFile));
        foreach (string document in documents)
            withProject = withProject.AddDocument(
                DocumentId.CreateNewId(projectId), Path.GetFileName(document), "", filePath: document);

        return withProject;
    }
}
