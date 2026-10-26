using SlideRule.Cli.Pipeline;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     A source, a <see cref="ColdSolutionSource" /> unless another is passed, that counts how many workspaces a
///     run acquired and records what each one loaded, so "this run never opened MSBuild at all" and "this run
///     loaded only a closure" are observable without any timing.
/// </summary>
/// <remarks>
///     Zero is the fact every persisted-cache suite is really after: a hit replays the recorded spec
///     resolution and merges stored fragments, so the whole of MSBuild — the design-time build, the
///     workspace, the solution load — is skipped, and an acquisition count is the only observable that says
///     so without measuring a clock. The count is of calls, so it means the same over any inner source. The
///     default is cold because the warm pool answers a closure request with the whole solution, and what
///     <see cref="Loads" /> records about a closure is only true of a real one; a run whose assertions read
///     nothing but the count can take <see cref="OverWarmPool" />.
/// </remarks>
internal sealed class CountingSolutionSource(ISolutionSource inner) : ISolutionSource
{
    public CountingSolutionSource() : this(new ColdSolutionSource())
    {
    }

    /// <summary>A counter over <see cref="WarmWorkspacePool.Source" />.</summary>
    public static CountingSolutionSource OverWarmPool()
    {
        return new CountingSolutionSource(WarmWorkspacePool.Source);
    }

    /// <summary>How many times a run asked this source for a loaded solution, closures included.</summary>
    public int AcquireCount { get; private set; }

    /// <summary>The project files each closure request named, in request order.</summary>
    public List<IReadOnlyCollection<string>> ClosureRequests { get; } = [];

    /// <summary>
    ///     The names of the projects each acquisition loaded, in acquisition order, with whether the handle was a
    ///     closure: what a run actually paid MSBuild for.
    /// </summary>
    public List<(bool IsClosure, IReadOnlySet<string> Projects)> Loads { get; } = [];

    public async Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        AcquireCount++;
        return Recorded(await inner.AcquireAsync(solutionPath, ct));
    }

    public async Task<SolutionHandle> AcquireClosureAsync(
        string solutionPath, IReadOnlyCollection<string> projectFiles, CancellationToken ct)
    {
        AcquireCount++;
        ClosureRequests.Add(projectFiles);
        return Recorded(await inner.AcquireClosureAsync(solutionPath, projectFiles, ct));
    }

    private SolutionHandle Recorded(SolutionHandle handle)
    {
        Loads.Add((handle.IsClosure, handle.Solution.Projects.Select(project => project.Name).ToHashSet(StringComparer.Ordinal)));
        return handle;
    }
}
