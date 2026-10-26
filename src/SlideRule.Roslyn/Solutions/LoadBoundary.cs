using Microsoft.CodeAnalysis;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     A loaded solution as <see cref="LoadBoundary.Settle" /> leaves it: what its readers see, the framework each
///     multi-targeted project was loaded for, what the load produced against the solution file, and how many
///     unresolved references were stripped.
/// </summary>
internal sealed record SettledSolution(
    Solution Solution,
    IReadOnlyDictionary<ProjectId, string> TargetFrameworks,
    ProjectLoadReport Report,
    int StrippedAnalyzerReferences,
    int StrippedMetadataReferences);

/// <summary>
///     The steps every load runs between the solution a loader produced and the one its readers see, on the
///     MSBuild and the replay path alike, so the two cannot drift apart.
/// </summary>
internal static class LoadBoundary
{
    /// <summary>
    ///     Strips <paramref name="solution" />'s unresolved references, normalizes its project names and measures
    ///     what loaded against <paramref name="solutionPath" />, the solution file the load came from or was run
    ///     over, or null when none is known.
    /// </summary>
    // The three steps read and write disjoint facts (references; names; file and output paths), so the order
    // decides nothing; it is fixed here so that both paths run one sequence.
    internal static SettledSolution Settle(Solution solution, string? solutionPath)
    {
        (Solution stripped, int analyzerCount, int metadataCount) = solution.StripUnresolvedReferences();
        (Solution normalized, IReadOnlyDictionary<ProjectId, string> targetFrameworks) =
            stripped.NormalizeProjectNames();
        ProjectLoadReport report = ProjectLoadFailures.Detect(normalized, solutionPath);

        return new SettledSolution(normalized, targetFrameworks, report, analyzerCount, metadataCount);
    }
}
