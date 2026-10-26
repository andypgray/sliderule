using Microsoft.CodeAnalysis;
using SlideRule.Cli.SpecLoading;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Sessions;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Cli.Pipeline;

/// <summary>What an <see cref="ISolutionSource" /> hands back.</summary>
/// <remarks>
///     A <see cref="Solution" /> stays usable after its workspace is disposed, so a handle read in flight is
///     safe even once a later call has reloaded.
/// </remarks>
internal sealed class SolutionHandle(
    Solution solution,
    WorkspaceDiagnostics loadDiagnostics,
    IDisposable? owned,
    CanonicalProjectFiles projectFiles,
    Func<IReadOnlyCollection<string>, IReadOnlySet<string>?, CancellationToken, Task<SessionCodebase>>? warmCodebase = null,
    Func<string, Func<SessionSpecResolution>, SessionSpecResolution>? warmSpecResolution = null,
    IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
    bool isClosure = false,
    Func<IReadOnlySet<string>?>? warmDeclaredMembers = null) : IDisposable
{
    /// <summary>
    ///     Whether <see cref="Solution" /> holds only the projects a caller asked
    ///     <see cref="ISolutionSource.AcquireClosureAsync" /> for and their references, rather than the whole
    ///     solution. Its <see cref="LoadDiagnostics" /> then speak only for the projects it loaded.
    /// </summary>
    public bool IsClosure { get; } = isClosure;

    /// <summary>The loaded, unresolved-reference-stripped solution the command reads.</summary>
    public Solution Solution { get; } = solution;

    /// <summary>
    ///     The target framework each multi-target-framework project was loaded for, keyed by
    ///     <see cref="ProjectId" />; empty for a solution whose projects each target one framework.
    /// </summary>
    public IReadOnlyDictionary<ProjectId, string> TargetFrameworks { get; } =
        targetFrameworks ?? TargetFrameworkMaps.None;

    /// <summary>Each loaded project's canonical <c>.csproj</c> path, as the load resolved it.</summary>
    public CanonicalProjectFiles ProjectFiles { get; } = projectFiles;

    /// <summary>How well the workspace loaded. Its merge notes are empty: only extraction produces them.</summary>
    public WorkspaceDiagnostics LoadDiagnostics { get; } = loadDiagnostics;

    /// <summary>
    ///     The warm session's codebase producer over this call's snapshot (<see cref="SessionFragmentStore" />),
    ///     taking the caller's excluded project names and declared solution membership; null on the cold/one-shot
    ///     path, which walks the solution once per run.
    /// </summary>
    public Func<IReadOnlyCollection<string>, IReadOnlySet<string>?, CancellationToken, Task<SessionCodebase>>?
        WarmCodebase { get; } = warmCodebase;

    /// <summary>
    ///     The warm session's spec-resolution memo under this call's generation (<see cref="SpecResolutionCache" />),
    ///     taking the normalized <c>--spec</c> argument and the cold resolution as a callback, so what resolution
    ///     knows stays at the one seam that resolves; null on the cold/one-shot path, which runs the callback.
    /// </summary>
    public Func<string, Func<SessionSpecResolution>, SessionSpecResolution>? WarmSpecResolution { get; } =
        warmSpecResolution;

    /// <summary>
    ///     The warm session's declared solution membership under this call's generation, read once per generation;
    ///     null on the cold/one-shot path, which reads the solution file itself.
    /// </summary>
    public Func<IReadOnlySet<string>?>? WarmDeclaredMembers { get; } = warmDeclaredMembers;

    /// <summary>
    ///     Disposes the owned workspace on the cold path; a no-op when the source owns nothing — a warm
    ///     session outlives the call and keeps its snapshot.
    /// </summary>
    public void Dispose()
    {
        owned?.Dispose();
    }
}
