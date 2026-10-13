using SlideRule.Roslyn;

namespace SlideRule.Cli.Pipeline;

/// <summary>
///     The one-shot solution source: the plain load path (<see cref="WorkspaceLoader.LoadAsync" />) behind the
///     <see cref="ISolutionSource" /> seam. Every <see cref="AcquireAsync" /> opens a fresh
///     <see cref="LoadedSolution" /> and hands it to the returned handle to own, so a <c>using</c> in the
///     caller bounds the workspace to the call — the enforcement path's lifetime. Stateless; the default
///     source every runner falls back to when none is injected.
/// </summary>
internal sealed class ColdSolutionSource : ISolutionSource
{
    /// <inheritdoc />
    public async Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        var diagnostics = new List<string>();
        LoadedSolution loaded = await WorkspaceLoader.LoadAsync(solutionPath, diagnostics.Add, ct);
        return new SolutionHandle(
            loaded.Solution, solutionPath, loaded.LoadDiagnosticsWith(diagnostics), loaded,
            targetFrameworks: loaded.TargetFrameworks);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Loads the closure through a temporary solution filter, and the whole solution when the subset cannot
    ///     be expressed as one; only the closure load marks the handle.
    /// </remarks>
    public async Task<SolutionHandle> AcquireClosureAsync(
        string solutionPath, IReadOnlyCollection<string> projectFiles, CancellationToken ct)
    {
        var diagnostics = new List<string>();
        LoadedSolution? loaded = await WorkspaceLoader.LoadClosureAsync(solutionPath, projectFiles, diagnostics.Add, ct);
        if (loaded is null) return await AcquireAsync(solutionPath, ct);

        return new SolutionHandle(
            loaded.Solution, solutionPath, loaded.LoadDiagnosticsWith(diagnostics), loaded,
            targetFrameworks: loaded.TargetFrameworks, isClosure: true);
    }
}
