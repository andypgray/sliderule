using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Codebase;
using SlideRule.Roslyn;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     C# 15 code through the workspace, on the <c>CSharp15App</c> fixture solution: a union, a closed record
///     hierarchy, an extension indexer and a collection expression with arguments, declared in one project and
///     used from another. The compiler the tool carries does the binding, so a carried compiler older than the
///     language reads this code as C# 14 and the union drops out of the model.
/// </summary>
public sealed class CSharp15ExtractionTests
{
    private const string Pet = "CSharp15App.Domain.Pets.Pet";

    private static readonly string SolutionPath = FixtureBeds.CSharp15AppSolution;

    [Fact]
    public async Task GetCompilationAsync_TheCSharp15Projects_BindWithoutErrors()
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(SolutionPath, Ct);

        var errors = new List<string>();
        foreach (Project project in snapshot.Solution.Projects)
        {
            Compilation compilation = (await project.GetCompilationAsync(Ct))!;
            IEnumerable<string> projectErrors = compilation.GetDiagnostics(Ct)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => $"{project.Name}: {diagnostic}");
            errors.AddRange(projectErrors);
        }

        snapshot.Solution.Projects.Select(project => project.Name)
            .ShouldBe(["CSharp15App.Consumer", "CSharp15App.Domain"], ignoreOrder: true);
        errors.ShouldBeEmpty("the errors the carried compiler reports on C# 15 code its own SDK accepts");
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_AUnion_IsAStructReferencingItsCaseTypes()
    {
        CodebaseModel model = await FixtureCodebase.Of(SolutionPath);

        model.Type(Pet)
            .Kind.ShouldBe(TypeKind.Struct);
        model.ShouldHaveEdge(Pet, "CSharp15App.Domain.Pets.Cat")
            .ShouldHaveEdge(Pet, "CSharp15App.Domain.Pets.Dog");
    }
}
