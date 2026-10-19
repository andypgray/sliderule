using System.Xml.Linq;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over which projects under <c>examples/</c> a solution declares. Every one of them is
///     declared by the example it belongs to, with a single recorded exception: the xUnit adapter test
///     project under <c>Meridian.Quoting</c>, which is a member of nothing on purpose. Both halves are
///     asserted, because each is only safe while the other holds.
/// </summary>
/// <remarks>
///     The adapter project's csproj carries the reasoning for the exception, where somebody editing it will
///     meet it. The first arm is an inventory rather than a pin on one path, so a new project that lands in
///     no solution reds on the day it appears and the exception stays one line.
/// </remarks>
public sealed class ExampleProjectMembershipTests
{
    private const string AdapterProject =
        "examples/Meridian.Quoting/tests/Meridian.Quoting.ArchTests/Meridian.Quoting.ArchTests.csproj";

    private const string Workflow = ".github/workflows/ci.yml";

    // Every project path any tracked solution declares, repository-relative. Built once: the arms
    // below ask about it for each of the example projects, and the answer cannot change mid-run.
    private static readonly Lazy<IReadOnlySet<string>> LazyDeclaredProjects = new(DeclaredProjects);

    [Fact]
    public void EveryExampleProject_IsDeclaredByASolution_ExceptTheAdapter()
    {
        // Act
        List<string> undeclared = ExampleProjects()
            .Where(static path => path != AdapterProject)
            .Where(static path => !LazyDeclaredProjects.Value.Contains(path))
            .ToList();

        // Assert: an example project no solution declares is built by nothing, checked by nothing and
        // reached by no inspection — and it looks exactly like one that is, because the file is there
        // and it compiles when something else happens to reference it.
        undeclared.ShouldReportNothing(
            "Example project(s) that no solution declares, so nothing above them builds or inspects them");
    }

    [Fact]
    public void TheAdapterProject_IsDeclaredByNoSolution()
    {
        // Arrange: the arm is worthless if the project has been moved or deleted, because a path
        // nothing declares and a path that does not exist read identically from here.
        ExampleProjects()
            .ShouldContain(AdapterProject, $"{AdapterProject} is no longer a tracked project.");

        // Act: ignoring case, the other way from the inventory arm, since a declaration in any spelling makes
        // the project a member wherever the file system ignores case, and one member anywhere is the refusal.
        List<string> declaring = TrackedSolutions()
            .Where(static solution => DeclaredBy(solution)
                .Contains(AdapterProject, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Assert
        declaring.ShouldReportNothing(
            "The xUnit adapter test project is a member of no solution on purpose — its csproj says "
            + "why, at length. Declaring it makes it a second spec-project candidate, which turns "
            + "every spec-less check of this example into a \"Multiple spec projects found\" refusal, "
            + "and it obliges the committed drawing to grow a test node. Solution(s) now declaring it");
    }

    [Fact]
    public void TheAdapterProject_IsBuiltAndRunByTheExamplesWorkflow()
    {
        // Act
        string workflow = RepoRoot.ReadText(Workflow);

        // Assert: the coverage that makes membership of no solution safe. Nothing else in CI compiles
        // this project, so this line going missing removes it from the build entirely, in silence.
        workflow.ShouldContain(
            $"dotnet test {AdapterProject}",
            $"{Workflow} no longer runs the xUnit adapter test project. It is a member of no solution, "
            + "so this line is the only thing that builds it — without it the project is compiled by "
            + "nothing and its rule tests run nowhere, with no other gate saying so.");
    }

    private static IReadOnlyList<string> ExampleProjects()
    {
        return TrackedFiles.Projects
            .Where(static path => path.StartsWith("examples/", StringComparison.Ordinal))
            .ToList();
    }

    private static IReadOnlyList<string> TrackedSolutions()
    {
        return TrackedFiles.All
            .Where(static path => path.EndsWith(".slnx", StringComparison.Ordinal))
            .ToList();
    }

    // Ordinal, because the examples job builds every example solution on Linux, where a declared path that
    // differs from the tracked one in case names no file: a declaration that matches only ignoring case
    // declares the project on Windows and macOS alone.
    private static IReadOnlySet<string> DeclaredProjects()
    {
        return TrackedSolutions()
            .SelectMany(DeclaredBy)
            .ToHashSet(StringComparer.Ordinal);
    }

    // The projects one solution declares, as repository-relative paths — resolved against the
    // solution's own directory, because a declared path is relative to the file that declares it.
    private static IReadOnlyList<string> DeclaredBy(string solutionRelativePath)
    {
        string solutionPath = RepoRoot.Absolute(solutionRelativePath);
        string solutionDirectory = Path.GetDirectoryName(solutionPath)!;

        return XDocument.Load(solutionPath)
            .Descendants("Project")
            .Select(static element => (string?)element.Attribute("Path"))
            .Where(static path => path is not null)
            .Select(path => Path.Combine(solutionDirectory, path!.Replace('\\', '/')))
            .Select(RepoRoot.Relative)
            .ToList();
    }
}
