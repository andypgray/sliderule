using SlideRule.Hosting;

namespace SlideRule.Cli.Pipeline;

/// <summary>
///     The seam between a workspace command and the host that serves it. Callers <c>using</c> every handle it
///     returns, whatever the source (<see cref="SolutionHandle.Dispose" />).
/// </summary>
internal interface ISolutionSource
{
    /// <summary>
    ///     Returns the already-discovered absolute solution at <paramref name="solutionPath" />, loaded. Load
    ///     failures surface exactly as the cold path raises them, preserving per-call error-text parity.
    /// </summary>
    /// <remarks>
    ///     Discovery is the caller's — <see cref="CodebaseSource" /> performs it once, before any source is
    ///     asked for anything, so a discovery failure is raised at the one place that can raise it and a warm
    ///     host does not repeat the ancestor walk on every tool call.
    /// </remarks>
    Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct);

    /// <summary>
    ///     Returns the solution at <paramref name="solutionPath" /> with at least <paramref name="projectFiles" />
    ///     and their transitive references loaded. A source that can load that subset alone marks the handle
    ///     <see cref="SolutionHandle.IsClosure" />; the default serves the whole solution, which is always
    ///     correct and only slower.
    /// </summary>
    Task<SolutionHandle> AcquireClosureAsync(
        string solutionPath, IReadOnlyCollection<string> projectFiles, CancellationToken ct)
    {
        return AcquireAsync(solutionPath, ct);
    }

    /// <summary>
    ///     The validated model behind <paramref name="specDllPath" />. The default is a full one-shot load; a
    ///     host whose lifetime spans many runs overrides it to cache the model against the DLL's file stamp,
    ///     since a spec changes only when its project is rebuilt. Load failures surface identically either way.
    /// </summary>
    ArchitectureModel LoadSpecModel(string specDllPath)
    {
        return ModelPipeline.LoadModel(specDllPath);
    }
}
