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
    ///     Removes the projects <paramref name="solution" />'s default configuration does not build, strips its
    ///     unresolved references, normalizes its project names and measures what loaded against
    ///     <paramref name="solutionPath" />, the solution file the load came from or was run over, or null when none
    ///     is known.
    /// </summary>
    // The removal comes first, so that no later step, and no reader of the load, sees a project the build skipped:
    // a restore of the solution can skip it too, and every package-backed fact about it would then read as a
    // restore failure. The
    // remaining steps read and write disjoint facts (references; names; file and output paths), so their order
    // decides nothing; it is fixed here so that both paths run one sequence. The project files are resolved once,
    // here, for the steps that compare them and for every reader of the load after them.
    internal static SettledSolution Settle(Solution solution, string? solutionPath)
    {
        SolutionMembership? membership = solutionPath is not null && SolutionProjectFileParser.OwnsFormat(solutionPath)
            ? SolutionProjectFileParser.ReadDeclaredMembership(solutionPath)
            : null;

        CanonicalProjectFiles projectFiles = CanonicalProjectFiles.Of(solution);
        Solution built = membership is null ? solution : WithoutUnbuilt(solution, projectFiles, membership.NotBuilt);
        (Solution stripped, int analyzerCount, int metadataCount) = built.StripUnresolvedReferences();
        (Solution normalized, IReadOnlyDictionary<ProjectId, string> targetFrameworks) =
            stripped.NormalizeProjectNames(projectFiles);
        ProjectLoadReport report = ProjectLoadFailures.Detect(normalized, projectFiles, membership);

        return new SettledSolution(
            normalized, targetFrameworks, projectFiles, report, analyzerCount, metadataCount);
    }

    // A not-built project leaves even when a built one references it: a solution build neither builds a project its
    // configuration disables nor passes its assembly to a project that references it, because MSBuild's
    // AssignProjectConfiguration defaults OnlyReferenceAndBuildProjectsEnabledInSolutionConfiguration to true
    // (measured on the .NET 10 SDK). Only the restore follows such a reference.
    private static Solution WithoutUnbuilt(
        Solution solution, CanonicalProjectFiles projectFiles, IReadOnlyList<string> notBuilt)
    {
        if (notBuilt.Count == 0) return solution;

        IReadOnlySet<string> unbuiltFiles = SpecExclusion.CanonicalMemberSet(notBuilt);
        List<ProjectId> unbuilt = solution.Projects
            .Where(project => projectFiles.PathOf(project) is { } file && unbuiltFiles.Contains(file))
            .Select(project => project.Id)
            .ToList();

        return unbuilt.Aggregate(solution, (remaining, id) => remaining.RemoveProject(id));
    }
}
