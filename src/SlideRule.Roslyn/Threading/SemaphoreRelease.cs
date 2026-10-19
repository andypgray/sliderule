namespace SlideRule.Roslyn.Threading;

/// <summary>
///     A held <see cref="SemaphoreSlim" /> slot, released once on dispose. A default instance holds nothing
///     and releases nothing.
/// </summary>
internal readonly struct SemaphoreRelease : IDisposable
{
    private readonly SemaphoreSlim? _gate;

    /// <summary>Wraps a slot of <paramref name="gate" /> the caller has already acquired.</summary>
    internal SemaphoreRelease(SemaphoreSlim gate)
    {
        _gate = gate;
    }

    public void Dispose()
    {
        _gate?.Release();
    }
}
