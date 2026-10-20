using SlideRule.Cli.Pipeline;

namespace SlideRule.Cli.Replay;

/// <summary>
///     Wraps a cold run's <see cref="ISolutionSource" /> to print a one-line <c>warning: {notice}</c> the
///     first — and only — time a workspace is actually acquired.
/// </summary>
/// <remarks>
///     This is the shape a persisted capture takes when it is <em>invalid</em> (stale or unreadable): the run
///     falls back to a design-time build, and the notice must land exactly once, at acquisition time rather
///     than at startup, so a fragment-cache hit — which acquires no workspace — prints nothing and stays
///     byte-identical to a plain cached run. A closure acquire that cannot stand in for the whole solution is
///     followed by a whole-solution acquire, and the guard flag keeps that run to one notice.
/// </remarks>
internal sealed class NoticingSolutionSource(string notice, TextWriter error, ISolutionSource inner) : ISolutionSource
{
    private bool _noticed;

    /// <inheritdoc />
    public Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        Notice();
        return inner.AcquireAsync(solutionPath, ct);
    }

    /// <inheritdoc />
    public Task<SolutionHandle> AcquireClosureAsync(
        string solutionPath, IReadOnlyCollection<string> projectFiles, CancellationToken ct)
    {
        Notice();
        return inner.AcquireClosureAsync(solutionPath, projectFiles, ct);
    }

    private void Notice()
    {
        if (_noticed) return;

        error.WriteLine($"warning: {notice}");
        _noticed = true;
    }
}
