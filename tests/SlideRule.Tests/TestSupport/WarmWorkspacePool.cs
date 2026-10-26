using SlideRule.Cli;
using SlideRule.Cli.Pipeline;
using SlideRule.Roslyn;

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
///         <b>Bounded.</b> A new session evicts the least recently used once <see cref="WorkspacePool.Capacity" />
///         sessions are live, because each holds an <c>MSBuildWorkspace</c> and its out-of-process BuildHost. Eviction
///         passes over one key, this repo's own solution: it is the single most expensive load there is, and
///         its consumers sit far apart in a run, with enough leased fixture copies between them, each a fresh
///         key, that recency alone evicts it before the next consumer arrives. It still counts toward
///         <see cref="WorkspacePool.Capacity" />, and <see cref="DropUnder" /> and <see cref="DropAll" /> still
///         release it. A session with a call in flight is never evicted either, so when every session but the
///         pinned one is mid-call the pool runs over <see cref="WorkspacePool.Capacity" />, by at most the
///         number of callers.
///     </para>
/// </remarks>
internal static class WarmWorkspacePool
{
    private static readonly WorkspacePool Shared = new(Path.GetFullPath(RepoRoot.Solution));

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
    internal static long ReuseCount => Shared.ReuseCount;

    /// <summary>The process-wide pool's <see cref="WorkspacePool.GetCurrentAsync" />.</summary>
    internal static Task<WorkspaceSnapshot> GetCurrentAsync(string solutionPath, CancellationToken ct)
    {
        return Shared.GetCurrentAsync(solutionPath, ct);
    }

    /// <summary>The process-wide pool's <see cref="WorkspacePool.DropUnder" />.</summary>
    internal static void DropUnder(string directory)
    {
        Shared.DropUnder(directory);
    }

    /// <summary>The process-wide pool's <see cref="WorkspacePool.DropAll" />.</summary>
    internal static void DropAll()
    {
        Shared.DropAll();
    }

    /// <summary>
    ///     A pool of its own that pins no key, for a test class whose subject is the pool itself: no class running
    ///     beside it can evict its sessions. The caller disposes it.
    /// </summary>
    internal static WorkspacePool Private()
    {
        return new WorkspacePool(pinnedKey: null);
    }

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
                snapshot.Solution, snapshot.LoadDiagnostics, null, snapshot.ProjectFiles,
                targetFrameworks: snapshot.TargetFrameworks);
        }
    }
}
