using SlideRule.Cli.Pipeline;
using SlideRule.Cli.SpecLoading;
using SlideRule.Hosting;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Sessions;
using SlideRule.Roslyn.Solutions;

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
    private readonly Lock membershipGate = new();

    private DeclaredMembership? membership;

    /// <summary>
    ///     How many times this source read the solution's declared membership from disk, once per workspace
    ///     generation in the steady state; never printed.
    /// </summary>
    internal int MembershipReads { get; private set; }

    /// <inheritdoc />
    public async Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        WorkspaceSnapshot snapshot = await session.GetCurrentAsync(solutionPath, ct);
        return new SolutionHandle(
            snapshot.Solution, snapshot.LoadDiagnostics, null, snapshot.ProjectFiles,
            (exclude, declaredMembers, token) => store.GetCodebaseAsync(snapshot, exclude, declaredMembers, token),
            (normalizedSpec, resolveFully) => resolutions.Resolve(normalizedSpec, snapshot.Generation, resolveFully),
            snapshot.TargetFrameworks,
            warmDeclaredMembers: () => DeclaredMembersFor(solutionPath, snapshot.Generation));
    }

    /// <inheritdoc />
    public ArchitectureModel LoadSpecModel(string specDllPath)
    {
        return specs.Load(specDllPath);
    }

    /// <summary>
    ///     The solution's declared membership, read once per <paramref name="generation" />. The generation is the
    ///     whole key: the session stats the solution file and every project file, so a change to any of them, and a
    ///     different solution path, reloads and bumps it. The set is canonicalized once and then frozen for the
    ///     generation, as the snapshot's own project files are.
    /// </summary>
    /// <remarks>
    ///     The read runs outside the gate, as <see cref="SpecResolutionCache" />'s does: two callers racing one miss
    ///     both read, and the last one recorded wins.
    /// </remarks>
    private IReadOnlySet<string>? DeclaredMembersFor(string solutionPath, long generation)
    {
        DeclaredMembership? cached;
        lock (membershipGate)
        {
            cached = membership;
        }

        if (cached is not null && cached.Generation == generation) return cached.Members;

        IReadOnlySet<string>? members = SpecExclusion.TryReadDeclaredMembers(solutionPath);

        lock (membershipGate)
        {
            membership = new DeclaredMembership(generation, members);
            MembershipReads++;
        }

        return members;
    }

    private sealed record DeclaredMembership(long Generation, IReadOnlySet<string>? Members);
}
