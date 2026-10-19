using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Shouldly;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="SolutionExtensions.StripUnresolvedReferences" /> and
///     <see cref="SolutionExtensions.NormalizeProjectNames" /> over a pure <see cref="AdhocWorkspace" />
///     graph. An unresolved analyzer reference (which would crash Roslyn's cross-project traversal APIs) is
///     removed from every project and counted, and the cleaned solution is returned without touching disk. The
///     unresolved-metadata arm needs a workspace-resolved reference an AdhocWorkspace cannot mint, so it stays
///     integration-tier — exercised end-to-end by the real workspace/replay loads. A replayed project's
///     frameworks are read from its compilations' output directories, and none is reported when they share one.
/// </summary>
public sealed class SolutionExtensionsTests
{
    [Fact]
    public void StripUnresolvedReferences_UnresolvedAnalyzer_IsRemovedAndCounted()
    {
        // Arrange — a one-project solution carrying a single unresolved analyzer reference.
        using var workspace = new AdhocWorkspace();
        ProjectInfo projectInfo = ProjectInfo
            .Create(ProjectId.CreateNewId(), VersionStamp.Default, "P", "P", LanguageNames.CSharp)
            .WithAnalyzerReferences([new UnresolvedAnalyzerReference("missing-analyzer.dll")]);
        Project project = workspace.AddProject(projectInfo);

        // Act
        (Solution stripped, int analyzerCount, int metadataCount) = project.Solution.StripUnresolvedReferences();

        // Assert — the unresolved analyzer is gone, counted once; nothing to strip on the metadata side.
        analyzerCount.ShouldBe(1);
        metadataCount.ShouldBe(0);
        stripped.GetProject(project.Id)
            .ShouldNotBeNull()
            .AnalyzerReferences.ShouldBeEmpty();
    }

    [Fact]
    public void NormalizeProjectNames_ReplayedFrameworksInTheirOwnOutputDirectories_ReportsEach()
    {
        // Arrange: the replay reader's shape. One project file, two compilations, no discriminator in either
        // name, and each output path ending in its framework's directory.
        using TempDirectory temp = TestTempRoot.Fresh("normalize-own-directories");
        string projectFile = WriteProjectFile(temp);
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(
            Replayed(projectFile, temp.PathOf("obj", "Debug", "net10.0", "Core.dll")),
            Replayed(projectFile, temp.PathOf("obj", "Debug", "netstandard2.0", "Core.dll")));

        // Act
        (_, IReadOnlyDictionary<ProjectId, string> frameworks) = solution.NormalizeProjectNames();

        // Assert
        frameworks.Values
            .Order(StringComparer.Ordinal)
            .ShouldBe(["net10.0", "netstandard2.0"]);
    }

    [Fact]
    public void NormalizeProjectNames_ReplayedFrameworksSharingOneOutputDirectory_ReportsNone()
    {
        // Arrange: a project that leaves the framework out of its output path builds every framework into the
        // configuration directory, so both output paths end in Debug and name no framework.
        using TempDirectory temp = TestTempRoot.Fresh("normalize-shared-directory");
        string projectFile = WriteProjectFile(temp);
        string sharedOutput = temp.PathOf("obj", "Debug", "Core.dll");
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(
            Replayed(projectFile, sharedOutput), Replayed(projectFile, sharedOutput));

        // Act
        (_, IReadOnlyDictionary<ProjectId, string> frameworks) = solution.NormalizeProjectNames();

        // Assert
        frameworks.ShouldBeEmpty();
    }

    private static string WriteProjectFile(TempDirectory temp)
    {
        return temp.WriteFile(["Core.csproj"], """<Project Sdk="Microsoft.NET.Sdk" />""");
    }

    private static ProjectInfo Replayed(string projectFile, string outputFilePath)
    {
        return AdhocSolution.Empty("Core", projectFile)
            .WithOutputFilePath(outputFilePath);
    }
}
