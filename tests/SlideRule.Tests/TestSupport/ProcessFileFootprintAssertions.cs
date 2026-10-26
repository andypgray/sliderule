using System.Diagnostics;
using Shouldly;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The Shouldly surface over <see cref="ProcessFileFootprint" />: one assertion, phrased as the property
///     it is checking rather than as a scan.
/// </summary>
/// <remarks>
///     Not attributed <c>[ShouldlyMethods]</c>: its one assertion throws its own
///     <see cref="ShouldAssertException" /> and makes no inner Shouldly call, so the attribute would change
///     nothing here.
/// </remarks>
internal static class ProcessFileFootprintAssertions
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    ///     Asserts that <paramref name="process" /> holds nothing under <paramref name="root" /> — no mapped
    ///     view, no open handle — polling until it is true or the budget expires.
    ///     <paramref name="inheritedHandles" /> (the launcher's own handles, taken before the process started)
    ///     and <paramref name="injectedProfilerDirectory" /> (where a profiler was loaded from, or null when
    ///     nothing is profiling) are excluded, because the process was given them rather than acquired them.
    /// </summary>
    /// <remarks>
    ///     Polling rather than a single shot because the subject is a live server: a tool call that has just
    ///     answered may still be closing a file its last statement opened, and a transient handle is not a
    ///     retained one. A clean first scan passes immediately, so the budget costs nothing in the green
    ///     case; only a genuine leak pays it. The failure names every offender with both spellings, because
    ///     when this reds the useful question is <em>which</em> build output is pinned.
    ///     <para>
    ///         The excused scan runs inside the loop rather than once over the caller's first look, because
    ///         the subject is re-scanned each poll and an unfiltered rescan would resurrect what was excused.
    ///     </para>
    /// </remarks>
    internal static void ShouldEventuallyHoldNoPathsUnder(
        this Process process,
        string root,
        IReadOnlyCollection<RetainedPath> inheritedHandles,
        string? injectedProfilerDirectory)
    {
        long start = Stopwatch.GetTimestamp();

        while (true)
        {
            IReadOnlyList<RetainedPath> retained = process.AcquiredPathsUnder(
                root, inheritedHandles, injectedProfilerDirectory);

            if (retained.Count == 0) return;

            if (Stopwatch.GetElapsedTime(start) >= Budget)
                throw new ShouldAssertException(Describe(process, root, retained));

            Thread.Sleep(PollInterval);
        }
    }

    private static string Describe(Process process, string root, IReadOnlyList<RetainedPath> retained)
    {
        IEnumerable<string> lines = retained
            .OrderBy(path => path.Scan)
            .ThenBy(path => path.ResolvedPath, StringComparer.OrdinalIgnoreCase)
            .Select(path => $"    [{path.Scan}] {path.ResolvedPath}{Environment.NewLine}        raw: {path.RawPath}");

        return $"PID {process.Id} still held {retained.Count} path(s) under '{root}' after "
               + $"{Budget.TotalSeconds:0.#}s:{Environment.NewLine}"
               + string.Join(Environment.NewLine, lines);
    }
}
