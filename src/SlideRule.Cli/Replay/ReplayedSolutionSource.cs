using Microsoft.CodeAnalysis;
using SlideRule.Cli.Pipeline;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Replay;

/// <summary>
///     The eager replay source: serves a <see cref="Solution" /> the gate has <em>already</em>
///     replayed from an explicit <c>--binlog</c>.
/// </summary>
/// <remarks>
///     Hands back a non-owning handle (<c>owned: null</c>): the gate owns the underlying
///     <see cref="Roslyn.Replay.ReplayedSolution" /> and disposes it once the run completes.
/// </remarks>
internal sealed class ReplayedSolutionSource(
    Solution replayedSolution,
    WorkspaceDiagnostics loadDiagnostics,
    IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null) : ISolutionSource
{
    /// <inheritdoc />
    public Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        return Task.FromResult(new SolutionHandle(
            replayedSolution, loadDiagnostics, null, targetFrameworks: targetFrameworks));
    }
}
