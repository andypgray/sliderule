using SlideRule.Cli.Pipeline;
using SlideRule.Cli.SpecLoading;
using SlideRule.Hosting;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Sessions;

namespace SlideRule.Cli.Mcp;

/// <summary>
///     The warm solution source: the host-managed counterpart to
///     <see cref="ColdSolutionSource" />. Each <see cref="AcquireAsync" /> serves the reconciled snapshot
///     the shared <see cref="WorkspaceSession" /> keeps warm across tool calls.
/// </summary>
/// <remarks>
///     The returned handle owns nothing (<c>owned: null</c>): the session outlives the call and keeps
///     the workspace, so disposing the handle is a no-op and a later reconcile cannot pull the solution out
///     from under an in-flight reader (a Roslyn <see cref="Microsoft.CodeAnalysis.Solution" /> survives its
///     workspace's disposal).
/// </remarks>
internal sealed class WarmSolutionSource(
    WorkspaceSession session,
    SessionFragmentStore store,
    SpecModelCache specs,
    SpecResolutionCache resolutions) : ISolutionSource
{
    /// <inheritdoc />
    public async Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        WorkspaceSnapshot snapshot = await session.GetCurrentAsync(solutionPath, ct);
        return new SolutionHandle(
            snapshot.Solution, snapshot.LoadDiagnostics, null,
            (exclude, declaredMembers, token) => store.GetCodebaseAsync(snapshot, exclude, declaredMembers, token),
            (normalizedSpec, resolveFully) => resolutions.Resolve(normalizedSpec, snapshot.Generation, resolveFully),
            snapshot.TargetFrameworks);
    }

    /// <inheritdoc />
    public ArchitectureModel LoadSpecModel(string specDllPath)
    {
        return specs.Load(specDllPath);
    }
}
