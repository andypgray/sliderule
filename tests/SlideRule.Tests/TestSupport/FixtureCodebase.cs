using System.Collections.Concurrent;
using SlideRule.Codebase;
using SlideRule.Internal;
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
///         test's cancellation token, because the task it produces outlives the test that first awaits it. It
///         is keyed by the solution's full path under the platform's path comparison, which must agree with
///         the key WarmWorkspacePool gives a session, so every spelling of one bed shares one walk.
///     </para>
/// </remarks>
internal static class FixtureCodebase
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<CodebaseModel>>> Extractions =
        new(PathComparison.Comparer);

    /// <summary>The model extracted from the solution at <paramref name="solutionPath" />.</summary>
    internal static Task<CodebaseModel> Of(string solutionPath)
    {
        string key = Path.GetFullPath(solutionPath);
        Lazy<Task<CodebaseModel>> extraction = Extractions.GetOrAdd(
            key, path => new Lazy<Task<CodebaseModel>>(() => ExtractAsync(path)));

        return extraction.Value;
    }

    private static async Task<CodebaseModel> ExtractAsync(string solutionPath)
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(solutionPath, CancellationToken.None);
        return await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, null, snapshot.TargetFrameworks, null, CancellationToken.None);
    }
}
