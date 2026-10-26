using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="ProjectLoadFailures.Detect" />'s two arms — and, under a solution filter, its second
///     answer — over a pure <see cref="AdhocWorkspace" /> graph plus a real solution file on disk. This is
///     the predicate the fail-closed gate keys on, rather than on MSBuild message text.
/// </summary>
/// <remarks>
///     <para>
///         The shapes asserted here are the ones a real load was measured to produce, not ones invented to
///         fit the code: a project whose evaluation failed carries no evaluated output path and no
///         intermediate assembly path, while every healthy project measured carried both — including a
///         non-SDK-style .NET Framework project and the <c>netstandard2.0</c> leg of a multi-target-framework
///         one, which are the two healthy shapes that would have been refused by a predicate keyed on
///         reference or analyzer counts instead.
///     </para>
///     <para>
///         The whole-solution behaviour is exercised end-to-end by <c>PartialLoadWorkspaceE2ETests</c> over a
///         solution that really does load partially; what is pinned here is the predicate itself, so a change
///         to it reds a fast unit test rather than only a minutes-long workspace one.
///     </para>
/// </remarks>
public sealed class ProjectLoadFailuresTests
{
    [Fact]
    public void Detect_ProjectWithAnEvaluatedOutputPath_IsNotAFailure()
    {
        // The negative control. A healthy project carries an evaluated output path whatever its reference or
        // analyzer counts, which is why those counts are not the predicate.
        using var workspace = new AdhocWorkspace();
        string csproj = ProjectPath("P", "P.csproj");
        Solution solution = workspace.WithProjects(AdhocSolution.Loaded("P", csproj));

        ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), null)
            .ShouldHaveLoadedEverything();
    }

    [Fact]
    public void Detect_ProjectWithNeitherOutputPath_IsAFailure()
    {
        // Roslyn's CreateEmpty shape: measured on a project naming an unresolvable SDK and on one whose
        // csproj XML is malformed. Both loaded as a Project with no documents, no analyzers and both paths
        // null, so the model is missing that project entirely while the load still "succeeded".
        using var workspace = new AdhocWorkspace();
        string csproj = ProjectPath("P", "P.csproj");
        Solution solution = workspace.WithProjects(AdhocSolution.Empty("P", csproj));

        ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), null)
            .ShouldHaveFailed(csproj);
    }

    [Fact]
    public void Detect_ProjectWithAnOutputPathButNoIntermediateOne_IsNotAFailure()
    {
        // Both facts are asked for rather than either, so a false positive — a refusal of a healthy solution,
        // the very defect this predicate replaces — needs two independent Roslyn facts to go missing at once.
        // This is the direction that separates the two spellings: under `||` it would red.
        using var workspace = new AdhocWorkspace();
        string csproj = ProjectPath("A", "A.csproj");
        string outputPath = ProjectPath("A", "bin", "A.dll");
        Solution solution = workspace.WithProjects(
            AdhocSolution.Empty("HasOutput", csproj).WithOutputFilePath(outputPath));

        ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), null)
            .ShouldHaveLoadedEverything();
    }

    [Fact]
    public void Detect_MultiTargetFrameworkProject_CollapsesToOneCsprojPath()
    {
        // One csproj behind several Projects: the reader has one file to go and fix, so the answer names it
        // once rather than once per framework.
        using var workspace = new AdhocWorkspace();
        string csproj = ProjectPath("P", "P.csproj");
        Solution solution = workspace.WithProjects(
            AdhocSolution.Empty("P(net10.0)", csproj),
            AdhocSolution.Empty("P(netstandard2.0)", csproj));

        ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), null)
            .ShouldHaveFailed(csproj);
    }

    [Fact]
    public void Detect_SolutionDeclaresAProjectThatDidNotLoad_NamesTheDeclaredPath()
    {
        // Arm 1, over a real solution file: a member whose .csproj is absent never becomes a Project at all,
        // so the loaded solution has nothing to inspect and only the declared membership can say it is gone.
        using TempDirectory temp = TestTempRoot.Fresh("declared-absent");
        string solutionPath = WriteSolution(temp, "Present", "Absent");
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(AdhocSolution.Loaded("Present", temp.PathOf("Present", "Present.csproj")));

        ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), solutionPath)
            .ShouldHaveFailed(temp.PathOf("Absent", "Absent.csproj"));
    }

    [Fact]
    public void Detect_SolutionWhoseEveryMemberLoaded_IsClean()
    {
        using TempDirectory temp = TestTempRoot.Fresh("declared-present");
        string solutionPath = WriteSolution(temp, "One", "Two");
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(
            AdhocSolution.Loaded("One", temp.PathOf("One", "One.csproj")),
            AdhocSolution.Loaded("Two", temp.PathOf("Two", "Two.csproj")));

        ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), solutionPath)
            .ShouldHaveLoadedEverything();
    }

    [Fact]
    public void Detect_SolutionFilter_NamesTheUnselectedMemberAsUncheckedRatherThanFailed()
    {
        // A .slnf legitimately loads a subset, so a member it did not select is not a failure — but it is not
        // nothing either. It lands in the second list, which narrows the verdict without gating it.
        using TempDirectory temp = TestTempRoot.Fresh("declared-filtered");
        string filterPath = WriteFilter(temp, ["Kept", "Dropped"], "Kept");
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(AdhocSolution.Loaded("Kept", temp.PathOf("Kept", "Kept.csproj")));

        ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), filterPath)
            .ShouldHaveLeftUnchecked(temp.PathOf("Dropped", "Dropped.csproj"));
    }

    [Fact]
    public void Detect_SolutionFilterSelectsAProjectThatDidNotLoad_IsAFailure()
    {
        // The strengthening the filter-aware arm buys: without it, the run asks for Kept, does not get it, and
        // exits green. It is sound to demand it because Roslyn refuses a filter naming a non-member outright,
        // so everything a well-formed filter selects is something the load was obliged to produce.
        using TempDirectory temp = TestTempRoot.Fresh("filtered-absent");
        string filterPath = WriteFilter(temp, ["Kept", "Dropped"], "Kept");
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.CurrentSolution;

        ProjectLoadReport report = ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), filterPath);

        report.Failed.ShouldBe([temp.PathOf("Kept", "Kept.csproj")]);
        report.Unchecked.ShouldBe([temp.PathOf("Dropped", "Dropped.csproj")]);
    }

    [Fact]
    public void Detect_SolutionFilterWithAnEmptyProjectsArray_ChecksTheWholeSolution()
    {
        // Roslyn's rule, which an intersection would invert: an empty projects array is not an empty
        // selection, it is no filtering at all. Read the other way this would report every member unchecked
        // and load none of them — the degraded answer arrived at from the opposite side.
        using TempDirectory temp = TestTempRoot.Fresh("filtered-empty");
        string filterPath = WriteFilter(temp, ["One", "Two"]);
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(
            AdhocSolution.Loaded("One", temp.PathOf("One", "One.csproj")),
            AdhocSolution.Loaded("Two", temp.PathOf("Two", "Two.csproj")));

        ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), filterPath)
            .ShouldHaveLoadedEverything();
    }

    [Fact]
    public void Detect_SolutionFilterWhoseUnselectedMemberLoadedAnyway_ReportsNoNarrowing()
    {
        // The measurement that refuted the obvious arithmetic: Roslyn loads a filter's projects PLUS their
        // transitive ProjectReference closure, so an unselected member routinely arrives anyway. Computing
        // the narrowing from the filter text would name it as skipped — a false claim of a gap in a run that
        // checked it. Subtracting what actually loaded is what makes that impossible.
        using TempDirectory temp = TestTempRoot.Fresh("filtered-transitive");
        string filterPath = WriteFilter(temp, ["Kept", "Pulled"], "Kept");
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(
            AdhocSolution.Loaded("Kept", temp.PathOf("Kept", "Kept.csproj")),
            AdhocSolution.Loaded("Pulled", temp.PathOf("Pulled", "Pulled.csproj")));

        ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), filterPath)
            .ShouldHaveLoadedEverything();
    }

    [Fact]
    public void Detect_BothArmsFire_ReturnsOnePathPerProjectInOrdinalOrder()
    {
        // The two arms are one answer, deduplicated by this OS's path rule and ordinal-sorted, so a refusal
        // reads the same however the failures arose and however the solution file happened to order them.
        using TempDirectory temp = TestTempRoot.Fresh("both-arms");
        string solutionPath = WriteSolution(temp, "Zed", "Absent");
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(AdhocSolution.Empty("Zed", temp.PathOf("Zed", "Zed.csproj")));

        ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), solutionPath)
            .ShouldHaveFailed(temp.PathOf("Absent", "Absent.csproj"), temp.PathOf("Zed", "Zed.csproj"));
    }

    [Fact]
    public void Detect_LoadedPathSpelledThroughALinkTarget_MatchesTheDeclaredMember()
    {
        // A replayed project carries the path its build was run under, which can be a symlink's target where
        // the solution was opened through the link (macOS's /var against /private/var). Arm 1 compares the
        // two canonically, so that member is not blamed, while a member that really is absent is still named
        // in the solution's own spelling.
        using TempDirectory real = TestTempRoot.Fresh("declared-real");
        using TempDirectory links = TestTempRoot.Fresh("declared-link");
        WriteSolution(real, "Present", "Absent");
        string via = links.PathOf("via");
        SymlinkSupport.CreateDirectorySymlink(via, real.Path);
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(AdhocSolution.Loaded("Present", real.PathOf("Present", "Present.csproj")));

        ProjectLoadFailures.Detect(solution, CanonicalProjectFiles.Of(solution), Path.Combine(via, "Solution.sln"))
            .ShouldHaveFailed(Path.Combine(via, "Absent", "Absent.csproj"));
    }

    // A classic .sln declaring one project line per name. Only the project paths are read, so the rest of the
    // file is the minimum a real solution carries.
    private static string WriteSolution(TempDirectory temp, params string[] projectNames)
    {
        var lines = new List<string>
        {
            "",
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "# Visual Studio Version 17"
        };
        foreach (string name in projectNames)
        {
            lines.Add(
                $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{name}\", "
                + $"\"{name}\\{name}.csproj\", \"{{11111111-1111-1111-1111-111111111111}}\"");
            lines.Add("EndProject");
        }

        string solutionPath = temp.PathOf("Solution.sln");
        File.WriteAllLines(solutionPath, lines);
        return solutionPath;
    }

    // A .slnf beside the solution it filters. Passing no selection writes an empty projects array — the
    // spelling Roslyn reads as "the whole solution", not as "nothing".
    private static string WriteFilter(TempDirectory temp, string[] members, params string[] selected)
    {
        WriteSolution(temp, members);

        IEnumerable<string> entries = selected.Select(name => $"\"{name}\\\\{name}.csproj\"");
        return temp.WriteFile(
            ["Filter.slnf"],
            $"{{\"solution\":{{\"path\":\"Solution.sln\",\"projects\":[{string.Join(",", entries)}]}}}}");
    }

    // A fabricated path, rooted for the OS running the test. Detect reports every path through
    // Path.GetFullPath, and a drive-letter literal is not rooted on a POSIX filesystem — the backslash is an
    // ordinary filename character there, so the whole literal would resolve against the working directory and
    // the path reported back would not be the one named here.
    private static string ProjectPath(params string[] segments)
    {
        string root = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";

        return Path.Combine([root, .. segments]);
    }
}
