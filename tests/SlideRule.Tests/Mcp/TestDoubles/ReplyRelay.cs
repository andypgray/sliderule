using System.IO.Pipelines;

namespace SlideRule.Tests.Mcp.TestDoubles;

/// <summary>
///     Copies one pipe into another after a delay, so a client reading <see cref="Reader" /> sees every byte the
///     server wrote, in order, only once the delay has passed.
/// </summary>
internal sealed class ReplyRelay : IAsyncDisposable
{
    private readonly Pipe _pipe = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _copy;

    /// <summary>Starts copying <paramref name="source" /> once <paramref name="delay" /> has passed.</summary>
    public ReplyRelay(PipeReader source, TimeSpan delay)
    {
        _copy = CopyAfterAsync(source, delay);
    }

    /// <summary>The delayed side, which completes when the source does or the relay is disposed.</summary>
    public PipeReader Reader => _pipe.Reader;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _copy;
        _stop.Dispose();
    }

    private async Task CopyAfterAsync(PipeReader source, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _stop.Token);
            await source.CopyToAsync(_pipe.Writer, _stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Disposal cancels the copy, the relay's ordinary end, and the finally still completes the writer.
        }
        finally
        {
            await _pipe.Writer.CompleteAsync();
        }
    }
}
