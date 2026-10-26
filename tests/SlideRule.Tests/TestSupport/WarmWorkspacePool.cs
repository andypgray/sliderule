using SlideRule.Cli;
using SlideRule.Cli.Pipeline;
using SlideRule.Internal;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Threading;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     A small, process-wide set of warm <see cref="WorkspaceSession" />s, keyed by solution path — the
///     harness's answer to the suite's dominant cost.
/// </summary>
/// <remarks>
///     <para>
///         <b>What it buys.</b> Measured: nearly every workspace open in a full run re-opened a solution
///         some earlier test in the same class had already loaded.
///     </para>
///     <para>
///         <b>Always reconciled.</b> Every acquisition goes through
///         <see cref="WorkspaceSession.GetCurrentAsync" />, the same reconcile against disk that the warm MCP
///         server runs in production, so a test that edits the tree (including
///         <see cref="TempFixtureWorkspace" />'s reset back to pristine between tests) sees exactly what a
///         freshly opened workspace would.
///     </para>
///     <para>
///         <b>The reset is visible.</b> <see cref="TempFixtureWorkspace" /> restores a mutated file with
///         <c>File.Copy</c>, which carries the pristine source's write time over, so the restored file
///         differs from the recorded fingerprint in both mtime and (usually) length. <c>FileFreshness</c>
///         treats any mtime difference as a change — a backwards timestamp included — so the sweep re-reads
///         and folds. Its one blind spot (equal mtime <em>and</em> equal length <em>and</em> different bytes
///         on an already-promoted file) cannot arise from a restore, which by definition puts the bytes
///         back. <c>WarmWorkspacePoolTests</c> pins this end to end.
///     </para>
///     <para>
///         <b>Not every test may use it.</b> A test that pins how many workspaces one call loads (its
///         <see cref="WorkspaceLoader.CountLoadsOnThisFlow" /> count), that needs a cold run as an oracle
///         against a warm one, or that has workspace loading itself as its subject must keep opening real
///         workspaces; each such test says so at its own call site.
///     </para>
///     <para>
///         <b>Bounded.</b> At most <see cref="Capacity" /> sessions are live, evicted least-recently-used
///         first, because each holds an <c>MSBuildWorkspace</c> and its out-of-process BuildHost. Eviction
///         passes over one key, this repo's own solution: it is the single most expensive load there is, and
///         its consumers sit far apart in a run, with enough leased fixture copies between them, each a fresh
///         key, that recency alone evicts it before the next consumer arrives. It still counts toward
///         <see cref="Capacity" />, and <see cref="DropUnder" /> and <see cref="DropAll" /> still release it.
///     </para>
/// </remarks>
internal static class WarmWorkspacePool
{
    private const int Capacity = 4;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    // Least-recently-used first; the tail is the most recent acquisition.
    private static readonly List<Entry> Live = [];

    private static readonly string PinnedKey = Path.GetFullPath(RepoRoot.Solution);

    /// <summary>
    ///     The pool as an <see cref="ISolutionSource" />, for handing to <see cref="CliEntry" /> or to any
    ///     runner that takes one. The handle it returns owns nothing (the pool outlives the call) and carries
    ///     no warm fragment extractor, so extraction stays the same full walk a cold run performs — only the
    ///     workspace open is shared.
    /// </summary>
    internal static ISolutionSource Source { get; } = new PooledSolutionSource();

    /// <summary>
    ///     The number of acquisitions served from an already-loaded session. With
    ///     <see cref="WorkspaceLoader.LoadCount" /> this is the pool's whole value proposition in two numbers.
    /// </summary>
    internal static long ReuseCount { get; private set; }

    /// <summary>
    ///     The reconciled snapshot for <paramref name="solutionPath" />, loading it if no session holds it.
    /// </summary>
    internal static async Task<WorkspaceSnapshot> GetCurrentAsync(string solutionPath, CancellationToken ct)
    {
        string key = Path.GetFullPath(solutionPath);

        using SemaphoreRelease held = await Gate.LockAsync(ct);
        WorkspaceSession session = await RentAsync(key);
        return await session.GetCurrentAsync(key, ct);
    }

    /// <summary>
    ///     Disposes every session whose solution lives under <paramref name="directory" />, releasing the
    ///     workspaces and their BuildHosts.
    /// </summary>
    /// <remarks>
    ///     Call this before rewriting or deleting a tree, so a warm workspace can never be the reason a
    ///     file is locked.
    /// </remarks>
    internal static void DropUnder(string directory)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;

        using SemaphoreRelease held = Gate.Lock();
        List<Entry> doomed = Live.Where(entry => entry.Key.StartsWith(prefix, PathComparison.Comparison))
            .ToList();
        foreach (Entry entry in doomed)
        {
            Live.Remove(entry);
            Dispose(entry);
        }
    }

    /// <summary>Disposes every live session.</summary>
    internal static void DropAll()
    {
        using SemaphoreRelease held = Gate.Lock();
        foreach (Entry entry in Live) Dispose(entry);
        Live.Clear();
    }

    // Caller holds the gate. Deliberately uncancellable: an eviction half-done would leave a workspace
    // undisposed and its BuildHost orphaned, and the work is a dispose plus a list edit.
    private static async Task<WorkspaceSession> RentAsync(string key)
    {
        int index = Live.FindIndex(entry => string.Equals(entry.Key, key, PathComparison.Comparison));
        if (index >= 0)
        {
            Entry existing = Live[index];
            Live.RemoveAt(index);
            Live.Add(existing); // most recently used
            ReuseCount++;
            return existing.Session;
        }

        while (Live.Count >= Capacity)
        {
            int oldest = Live.FindIndex(static entry => !string.Equals(entry.Key, PinnedKey, PathComparison.Comparison));
            Entry evicted = Live[oldest];
            Live.RemoveAt(oldest);
            await evicted.Session.DisposeAsync();
        }

        var minted = new Entry(key, new WorkspaceSession());
        Live.Add(minted);
        return minted.Session;
    }

    // Blocking dispose: the pool's sync entry points run between tests, never inside a workspace call, and a
    // session's own DisposeAsync is already bounded, so there is nothing here to deadlock against.
    private static void Dispose(Entry entry)
    {
        entry.Session.DisposeAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    private sealed record Entry(string Key, WorkspaceSession Session);

    /// <summary>
    ///     The pool behind the <see cref="ISolutionSource" /> seam: serve the pooled session's reconciled
    ///     snapshot for the solution the caller has already discovered.
    /// </summary>
    private sealed class PooledSolutionSource : ISolutionSource
    {
        public async Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
        {
            WorkspaceSnapshot snapshot = await GetCurrentAsync(solutionPath, ct);
            // The snapshot's whole load verdict, not a hand-picked part of it: composed without the project
            // lists, no warm-pool test could ever observe a failed project, a narrowed universe, or the
            // stamps and refusals keyed on either — the pool would silently answer "nothing to report".
            return new SolutionHandle(
                snapshot.Solution, snapshot.LoadDiagnostics, null,
                targetFrameworks: snapshot.TargetFrameworks);
        }
    }
}
