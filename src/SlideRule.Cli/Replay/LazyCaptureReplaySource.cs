using SlideRule.Cli.Pipeline;
using SlideRule.Roslyn.Replay;

namespace SlideRule.Cli.Replay;

/// <summary>
///     The lazy replay source: replays a persisted, structurally-valid build capture — but only
///     when a runner actually acquires a workspace. A fragment-cache hit never acquires, so it stays
///     replay-free and byte-identical to a plain cached run.
/// </summary>
/// <remarks>
///     Replays the capture's binlog copy on the one <see cref="AcquireAsync" /> call the runner makes, and
///     owns the <see cref="ReplayedSolution" /> that produces: the handle it hands back is non-owning
///     (<c>owned: null</c>) because the replay's reader backs the solution's lazy per-document text loaders,
///     so releasing it at handle scope would come too early — <see cref="Dispose" /> releases it at run scope
///     instead.
/// </remarks>
internal sealed class LazyCaptureReplaySource(string binlogCopyPath) : ISolutionSource, IDisposable
{
    private ReplayedSolution? _replayed;

    /// <summary>
    ///     Releases the replayed solution — its workspace and binlog reader — once the run that acquired it is
    ///     done. A run that never acquired, and one whose replay failed, materialised nothing to release.
    /// </summary>
    public void Dispose()
    {
        _replayed?.Dispose();
    }

    /// <inheritdoc />
    public Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        // The handle handed out does not own the replay, so a second replay could neither replace the first
        // (whose handle may still be reading through its reader) nor be released alongside it.
        if (_replayed is not null)
            throw new InvalidOperationException("A capture replay source serves one acquire per run.");

        var diagnostics = new List<string>();
        ReplayedSolution replayed;
        try
        {
            replayed = BinlogReplayer.Replay(binlogCopyPath, solutionPath, diagnostics.Add, ct);
        }
        // Cancellation is the caller's, not a bad capture: let it propagate rather than be reported as a
        // replay failure that would send the gate off to re-run the whole thing cold.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CaptureReplayFailedException(BinlogReplayMessages.CaptureReplayFailedNotice(ex.Message), ex);
        }

        _replayed = replayed;
        var source = new ReplayedSolutionSource(
            replayed.Solution, replayed.LoadDiagnosticsWith(diagnostics), replayed.TargetFrameworks);
        return source.AcquireAsync(solutionPath, ct);
    }
}
