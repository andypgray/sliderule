using Microsoft.CodeAnalysis;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     A loaded solution as <see cref="LoadBoundary.Settle" /> leaves it: what its readers see, the framework each
///     multi-targeted project was loaded for, each project's canonical file, what the load produced against the
///     solution file, and how many unresolved references were stripped.
/// </summary>
internal sealed record SettledSolution(
    Solution Solution,
    IReadOnlyDictionary<ProjectId, string> TargetFrameworks,
    CanonicalProjectFiles ProjectFiles,
    ProjectLoadReport Report,
    int StrippedAnalyzerReferences,
    int StrippedMetadataReferences);

/// <summary>
///     The steps between the solution a loader produced and the one its readers see, kept in one place so the
///     MSBuild and the replay path settle a solution alike and cannot drift apart.
/// </summary>
internal static class LoadBoundary
{
    /// <summary>
    ///     What <paramref name="solutionPath" />, the solution file a load came from or was run over, declares, or null
    ///     when none is known or the parser does not own its format. Each load reads it once, for every step below.
    /// </summary>
    internal static SolutionMembership? ReadMembership(string? solutionPath)
    {
        return solutionPath is not null && SolutionProjectFileParser.OwnsFormat(solutionPath)
            ? SolutionProjectFileParser.ReadDeclaredMembership(solutionPath)
            : null;
    }

    /// <summary>
    ///     Removes the projects <paramref name="membership" /> says the solution's default configuration does not
    ///     build. Only the MSBuild load runs it, because the workspace opens every entry whatever the configuration
    ///     says; a replay holds a not-built project only when its binlog compiled one, and keeps it.
    /// </summary>
    // It runs before Settle, so that no step after it, and no reader of the load, sees a project the build skipped: a
    // restore of the solution can skip it too, and every package-backed fact about it would then read as a restore
    // failure. A not-built project leaves even when a built one references it: a solution build neither builds a
    // project its configuration disables nor passes its assembly to a project that references it, because MSBuild's
    // AssignProjectConfiguration defaults OnlyReferenceAndBuildProjectsEnabledInSolutionConfiguration to true
    // (measured on the .NET 10 SDK). Only the restore follows such a reference.
    internal static Solution WithoutUnbuilt(Solution solution, SolutionMembership? membership)
    {
        if (membership is null || membership.NotBuilt.Count == 0) return solution;

        CanonicalProjectFiles projectFiles = CanonicalProjectFiles.Of(solution);
        IReadOnlySet<string> unbuiltFiles = SpecExclusion.CanonicalMemberSet(membership.NotBuilt);
        List<ProjectId> unbuilt = solution.Projects
            .Where(project => projectFiles.PathOf(project) is { } file && unbuiltFiles.Contains(file))
            .Select(project => project.Id)
            .ToList();

        return unbuilt.Aggregate(solution, (remaining, id) => remaining.RemoveProject(id));
    }

    /// <summary>
    ///     Strips <paramref name="solution" />'s unresolved references, normalizes its project names and measures what
    ///     loaded against <paramref name="membership" /> (<see cref="ReadMembership" />'s answer) and
    ///     <paramref name="otherLanguageProjects" />, the project files in another language the load could not reach:
    ///     the compiler calls a replay declined, or the referenced projects the workspace opened in no language.
    /// </summary>
    // The steps read and write disjoint facts (references; names; parse options; file and output paths), so their
    // order decides nothing, except that the defines step needs the framework map the name step returns; it is fixed
    // here so that both paths run one sequence. The project files are resolved once, here, for the steps that compare
    // them and for every reader of the load after them.
    internal static SettledSolution Settle(
        Solution solution, SolutionMembership? membership, IReadOnlyCollection<string> otherLanguageProjects)
    {
        CanonicalProjectFiles projectFiles = CanonicalProjectFiles.Of(solution);
        (Solution stripped, int analyzerCount, int metadataCount) = solution.StripUnresolvedReferences();
        (Solution normalized, IReadOnlyDictionary<ProjectId, string> targetFrameworks) =
            stripped.NormalizeProjectNames(projectFiles);
        Solution defined = VisualBasicDefines.Restore(normalized, targetFrameworks);
        ProjectLoadReport report = ProjectLoadFailures.Detect(defined, projectFiles, membership, otherLanguageProjects);

        return new SettledSolution(
            defined, targetFrameworks, projectFiles, report, analyzerCount, metadataCount);
    }
}
