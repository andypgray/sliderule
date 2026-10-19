using SlideRule.Roslyn.Threading;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The blocking counterpart of <see cref="SemaphoreSlimExtensions.LockAsync" />, for the harness's
///     synchronous entry points. Test-only: the product's hosts never block on a semaphore.
/// </summary>
internal static class SemaphoreSlimTestExtensions
{
    /// <summary>Waits for a slot of <paramref name="gate" /> and returns it as a value that releases it on dispose.</summary>
    internal static SemaphoreRelease Lock(this SemaphoreSlim gate)
    {
        gate.Wait();
        return new SemaphoreRelease(gate);
    }
}
