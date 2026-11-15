using Microsoft.CodeAnalysis;
using SlideRule.Cli.Pipeline;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Replay;

namespace SlideRule.Cli.Replay;

/// <summary>
///     The eager replay source: serves a <see cref="Solution" /> the gate has <em>already</em>
///     replayed from an explicit <c>--binlog</c>.
/// </summary>
/// <remarks>
///     Hands back a non-owning handle (<c>owned: null</c>): whoever replayed the underlying
///     <see cref="ReplayedSolution" /> owns it and disposes it once the run completes.
/// </remarks>
internal sealed class ReplayedSolutionSource(ReplayedSolution replayed, WorkspaceDiagnostics loadDiagnostics)
    : ISolutionSource
{
    /// <inheritdoc />
    public Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        return Task.FromResult(new SolutionHandle(
            replayed.Solution, loadDiagnostics, null, targetFrameworks: replayed.TargetFrameworks,
            projectFiles: replayed.ProjectFiles, compilerAssemblies: replayed.CompilerAssemblies));
    }
}
