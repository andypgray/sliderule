namespace SlideRule.Roslyn.Threading;

/// <summary>Scoped acquisition of a <see cref="SemaphoreSlim" />.</summary>
internal static class SemaphoreSlimExtensions
{
    /// <summary>
    ///     Waits for a slot of <paramref name="gate" /> and returns it as a value that releases it on dispose,
    ///     so the release cannot drift from the wait:
    ///     <c>using SemaphoreRelease held = await gate.LockAsync(ct).ConfigureAwait(false);</c>. A cancelled wait
    ///     acquires nothing and so has nothing to release.
    /// </summary>
    internal static async Task<SemaphoreRelease> LockAsync(this SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new SemaphoreRelease(gate);
    }
}
