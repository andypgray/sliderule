namespace SlideRule.Cli.Mcp.Infrastructure;

/// <summary>
///     The one slot warm work runs in: every tool call, every hook check the server answers, and its warm-up.
///     Tool calls wait for it as long as they must; a hook check waits only its discovery limit and is otherwise
///     declined, so a stop never queues behind a long tool call. An admission that does not serialize admits every
///     caller at once, for a server whose warm workspace is off and that answers no hook.
/// </summary>
internal sealed class WarmWorkAdmission(bool serializes = true) : IDisposable
{
    private readonly SemaphoreSlim? slot = serializes ? new SemaphoreSlim(1, 1) : null;

    /// <summary>Waits for the slot, held until the returned lease is disposed.</summary>
    public async Task<Lease> EnterAsync(CancellationToken ct)
    {
        if (slot is not null) await slot.WaitAsync(ct);

        return new Lease(slot);
    }

    /// <summary>Takes the slot if it frees within <paramref name="wait" />; null when it did not.</summary>
    public async Task<Lease?> TryEnterAsync(TimeSpan wait, CancellationToken ct)
    {
        if (slot is null) return new Lease(null);

        return await slot.WaitAsync(wait, ct) ? new Lease(slot) : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        slot?.Dispose();
    }

    /// <summary>A hold on the slot; disposing it gives the slot up, once, and does nothing where none was taken.</summary>
    internal sealed class Lease(SemaphoreSlim? slot) : IDisposable
    {
        private int released;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) slot?.Release();
        }
    }
}
