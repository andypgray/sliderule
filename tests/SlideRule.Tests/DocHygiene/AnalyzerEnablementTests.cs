using System.Text.RegularExpressions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate that holds every project under <c>src/</c> to running the .NET analyzers, so the
///     severities <c>analyzers.globalconfig</c> promotes are read by something.
/// </summary>
/// <remarks>
///     <para>
///         The SDK turns its own analyzers on by default only from net5.0 up, and most of what this
///         repository ships targets netstandard2.0. Naming the severity file is therefore necessary and not
///         sufficient: on a project the SDK left off, every promotion in that file is inert. Nothing reports
///         it: the file is named, the build is green, and the rules never run.
///     </para>
///     <para>
///         Nothing outside the build reads this property, so the question asked is the one MSBuild
///         answers, not what a csproj literally carries. That is also what lets the declaration live once,
///         in <c>src/Directory.Build.props</c>, rather than in every project file.
///     </para>
///     <para>
///         The solution decides the set. Read through <see cref="SolutionInventory" />, so a project added
///         under <c>src/</c> is held from the moment the solution declares it, with nobody adding it to a
///         list here.
///     </para>
/// </remarks>
public sealed class AnalyzerEnablementTests
{
    private const string Property = "EnableNETAnalyzers";

    // The frameworks the SDK analyzes without being asked: net5.0 and up, never netstandard or net4x. A
    // framework this does not match counts as one the SDK leaves off, so an unreadable one asks for the
    // declaration rather than assuming it is unnecessary.
    private static readonly Regex AnalyzedByDefault = new(@"^net([5-9]|[1-9]\d+)\.\d+$");

    [Fact]
    public void EverySrcProject_RunsTheNetAnalyzers()
    {
        // Arrange
        IReadOnlyList<SolutionProject> projects = SolutionInventory.SrcProjects;
        List<string> unanalyzed = new();

        // Act: an empty list would make this pass by reading no project at all rather than by finding every
        // one of them analyzed, so it is a finding.
        if (projects.Count == 0) unanalyzed.Add("The solution file declares nothing under src/.");

        IEnumerable<string> off = projects
            .Where(static project => !RunsTheAnalyzers(project))
            .Select(static project => $"{project.Name} ({project.TargetFramework})")
            .Order(StringComparer.Ordinal);
        unanalyzed.AddRange(off);

        // Assert
        unanalyzed.ShouldReportNothing(
            "Project(s) under src/ do not run the .NET analyzers, so every severity "
            + "analyzers.globalconfig promotes is inert there — the SDK leaves them off below net5.0");
    }

    // A project's own declaration wins over the one above it, and either wins over the SDK's default,
    // exactly as MSBuild evaluates them.
    private static bool RunsTheAnalyzers(SolutionProject project)
    {
        PropertyDeclaration? declared = SolutionInventory.Effective(project.CsprojPath, Property);

        return declared is null ? AnalyzedByDefault.IsMatch(project.TargetFramework) : IsTrue(declared.Value);
    }

    private static bool IsTrue(string value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
