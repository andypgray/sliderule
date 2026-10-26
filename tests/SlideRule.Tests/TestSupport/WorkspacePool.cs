using SlideRule.Internal;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Threading;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     One bounded set of warm sessions, least-recently-used first. <paramref name="pinnedKey" /> is the one
///     solution eviction passes over, or <c>null</c> for none. <see cref="WarmWorkspacePool" /> holds the
///     process-wide instance.
/// </summary>
internal sealed class WorkspacePool(string? pinnedKey) : IAsyncDisposable
{
    internal const int Capacity = 4;

    private readonly SemaphoreSlim _gate = new(1, 1);

    // Least-recently-used first; the tail is the most recent acquisition.
    private readonly List<Entry> _live = [];

    /// <summary>The number of acquisitions served from an already-loaded session.</summary>
    internal long ReuseCount { get; private set; }

    /// <summary>Disposes every live session, then the gate.</summary>
    public ValueTask DisposeAsync()
    {
        DropAll();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     The reconciled snapshot for <paramref name="solutionPath" />, loading it if no session holds it.
    /// </summary>
    internal async Task<WorkspaceSnapshot> GetCurrentAsync(string solutionPath, CancellationToken ct)
    {
        string key = Path.GetFullPath(solutionPath);
        Entry entry;
        using (await _gate.LockAsync(ct))
        {
            entry = await RentAsync(key);
            Interlocked.Increment(ref entry.Users);
        }

        // The gate covers the rent alone. The session's own gate serializes the callers of one solution and
        // shares one load between them, so no acquisition waits for another solution's load.
        try
        {
            return await entry.Session.GetCurrentAsync(key, ct);
        }
        finally
        {
            Interlocked.Decrement(ref entry.Users);
        }
    }

    /// <summary>
    ///     Disposes every session whose solution lives under <paramref name="directory" />, releasing the
    ///     workspaces and their BuildHosts.
    /// </summary>
    /// <remarks>
    ///     Call this before rewriting or deleting a tree, so a warm workspace can never be the reason a
    ///     file is locked. It disposes a matching session whether or not a call is in flight on it, so call
    ///     it only for a tree no other flow is reading.
    /// </remarks>
    internal void DropUnder(string directory)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;

        using SemaphoreRelease held = _gate.Lock();
        List<Entry> doomed = _live.Where(entry => entry.Key.StartsWith(prefix, PathComparison.Comparison))
            .ToList();
        foreach (Entry entry in doomed)
        {
            _live.Remove(entry);
            Dispose(entry);
        }
    }

    /// <summary>Disposes every live session, including any with a call in flight.</summary>
    internal void DropAll()
    {
        using SemaphoreRelease held = _gate.Lock();
        foreach (Entry entry in _live) Dispose(entry);
        _live.Clear();
    }

    // Caller holds the gate. Deliberately uncancellable: an eviction half-done would leave a workspace
    // undisposed and its BuildHost orphaned, and the work is a dispose plus a list edit.
    private async Task<Entry> RentAsync(string key)
    {
        int index = _live.FindIndex(entry => string.Equals(entry.Key, key, PathComparison.Comparison));
        if (index >= 0)
        {
            Entry existing = _live[index];
            _live.RemoveAt(index);
            _live.Add(existing); // most recently used
            ReuseCount++;
            return existing;
        }

        while (_live.Count >= Capacity)
        {
            int oldest = _live.FindIndex(entry => Volatile.Read(ref entry.Users) == 0
                                                  && !string.Equals(entry.Key, pinnedKey, PathComparison.Comparison));
            if (oldest < 0) break;

            Entry evicted = _live[oldest];
            _live.RemoveAt(oldest);
            await evicted.Session.DisposeAsync();
        }

        var minted = new Entry(key);
        _live.Add(minted);
        return minted;
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

    private sealed class Entry(string key)
    {
        internal string Key { get; } = key;

        internal WorkspaceSession Session { get; } = new();

        // Calls in flight on Session. Raised only under the pool's gate, so a zero read under that gate cannot
        // race a new renter.
        internal int Users;
    }
}
