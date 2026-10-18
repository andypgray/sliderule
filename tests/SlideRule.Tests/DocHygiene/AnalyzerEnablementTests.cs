using System.Text.RegularExpressions;
using Shouldly;
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

    private const string ScopedProps = "src/Directory.Build.props";

    // The frameworks the SDK analyzes without being asked: net5.0 and up, never netstandard or net4x. A
    // framework this does not match counts as one the SDK leaves off, so an unreadable one asks for the
    // declaration rather than assuming it is unnecessary.
    private static readonly Regex AnalyzedByDefault = new(@"^net([5-9]|[1-9]\d+)\.\d+$");

    [Fact]
    public void EverySrcProject_RunsTheNetAnalyzers()
    {
        // Arrange
        IReadOnlyList<SolutionProject> projects = SolutionInventory.SrcProjects;
        bool declaredAboveThem = DeclaredAboveThem();

        // Act
        List<string> unanalyzed = projects
            .Where(project => !RunsTheAnalyzers(project, declaredAboveThem))
            .Select(static project => $"{project.Name} ({project.TargetFramework})")
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        unanalyzed.ShouldReportNothing(
            "Project(s) under src/ do not run the .NET analyzers, so every severity "
            + "analyzers.globalconfig promotes is inert there — the SDK leaves them off below net5.0");
    }

    [Fact]
    public void SrcProjectList_IsNotEmpty()
    {
        // Assert: the arm above reports what a filter left behind, and an empty list would make it pass
        // by reading no project at all rather than by finding every one of them analyzed.
        SolutionInventory.SrcProjects.ShouldNotBeEmpty("The solution file declares nothing under src/.");
    }

    // A project's own declaration wins over the one above it, and both win over the SDK's default,
    // exactly as MSBuild evaluates them.
    private static bool RunsTheAnalyzers(SolutionProject project, bool declaredAboveThem)
    {
        IReadOnlyDictionary<string, string> own = SolutionInventory.Properties(project.CsprojPath);

        if (own.TryGetValue(Property, out string? declared)) return IsTrue(declared);

        return declaredAboveThem || AnalyzedByDefault.IsMatch(project.TargetFramework);
    }

    private static bool DeclaredAboveThem()
    {
        IReadOnlyDictionary<string, string> scoped = SolutionInventory.Properties(ScopedProps);

        return scoped.TryGetValue(Property, out string? declared) && IsTrue(declared);
    }

    private static bool IsTrue(string value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
