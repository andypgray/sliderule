using System.Collections.Concurrent;
using SlideRule.Codebase;
using SlideRule.Roslyn;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     One extracted model per fixture solution, shared by every class that reads that bed in place.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="WarmWorkspacePool" /> already shares the MSBuild load, which is the expensive half of
///         opening a solution — but not the Roslyn walk over it, which two classes reading one bed would
///         otherwise each pay for. The walk is a pure read: nothing here writes to the tree, so one model
///         answers both.
///     </para>
///     <para>
///         The extraction starts at most once per solution and every caller awaits that one task. It takes no
///         test's cancellation token, because the task it produces outlives the test that first awaits it.
///     </para>
/// </remarks>
internal static class FixtureCodebase
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<CodebaseModel>>> Extractions =
        new(StringComparer.Ordinal);

    /// <summary>The model extracted from the solution at <paramref name="solutionPath" />.</summary>
    internal static Task<CodebaseModel> Of(string solutionPath)
    {
        Lazy<Task<CodebaseModel>> extraction = Extractions.GetOrAdd(
            solutionPath, path => new Lazy<Task<CodebaseModel>>(() => ExtractAsync(path)));

        return extraction.Value;
    }

    private static async Task<CodebaseModel> ExtractAsync(string solutionPath)
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(solutionPath, CancellationToken.None);
        return await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, null, snapshot.TargetFrameworks, null, CancellationToken.None);
    }
}
