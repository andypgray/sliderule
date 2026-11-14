using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace SlideRule.Cli.Pipeline;

/// <summary>A span of a run that <c>check --timings</c> reports, in the order the report lists them.</summary>
internal enum TimedPhase
{
    Startup,
    Resolve,
    MsBuild,
    CacheRead,
    WorkspaceLoad,
    SpecResolve,
    SpecLoad,
    Extraction,
    CacheWrite,
    Check,
    Report
}

/// <summary>
///     How a run's codebase was obtained, for the line under the timings: the source's outcome, how many projects
///     this run extracted itself, and how many the model holds.
/// </summary>
internal readonly record struct CacheVerdict(CodebaseSourceOutcome Outcome, int Extracted, int Projects);

/// <summary>
///     The per-phase wall times of one <c>check --timings</c> run. <see cref="Begin" /> opens the run's scope, and
///     each <see cref="Measure" /> inside it adds its span to a phase; outside a scope a measure records nothing.
/// </summary>
/// <remarks>
///     Phases record exclusive time: a measure opened inside another is subtracted from its parent, so a phase is
///     never counted twice and the phases sum to at most the total. Measures nest on one flow and never overlap
///     concurrently.
/// </remarks>
internal sealed class PhaseTimings : IDisposable
{
    // Ambient, so a verb opts in by opening a scope at its entry, with no signature change below it.
    private static readonly AsyncLocal<PhaseTimings?> Current = new();

    private readonly Dictionary<TimedPhase, TimeSpan> phases = [];
    private readonly PhaseTimings? previous;
    private readonly long startedAt;
    // The innermost measure still running, which the caller of Measure owns and disposes.
#pragma warning disable CA2213
    private Span? open;
#pragma warning restore CA2213

    private PhaseTimings(PhaseTimings? previous, TimeSpan? startup)
    {
        this.previous = previous;
        startedAt = Stopwatch.GetTimestamp();
        if (startup is { } elapsed) phases[TimedPhase.Startup] = elapsed;
    }

    /// <summary>The verdict <see cref="RecordCache" /> recorded, or null where the run produced none.</summary>
    internal CacheVerdict? Cache { get; private set; }

    /// <summary>Closes the scope, restoring the one that was current when it opened.</summary>
    public void Dispose()
    {
        Current.Value = previous;
    }

    /// <summary>
    ///     Opens the timing scope for this flow, measuring <see cref="TimedPhase.Startup" /> as the time since the
    ///     process started.
    /// </summary>
    internal static PhaseTimings Begin()
    {
        var timings = new PhaseTimings(Current.Value, ProcessAge());
        Current.Value = timings;
        return timings;
    }

    /// <summary>
    ///     Times <paramref name="phase" /> until the returned value is disposed, adding to the time it has
    ///     already recorded; a no-op outside a scope.
    /// </summary>
    internal static IDisposable Measure(TimedPhase phase)
    {
        return Current.Value is { } timings ? timings.Open(phase) : NoSpan.Instance;
    }

    /// <summary>Records the run's cache verdict on the current scope; a no-op outside one.</summary>
    internal static void RecordCache(CacheVerdict verdict)
    {
        if (Current.Value is { } timings) timings.Cache = verdict;
    }

    /// <summary>
    ///     Writes the report lines to <paramref name="error" />, with the total measured from process start to now.
    /// </summary>
    internal void WriteTo(TextWriter error)
    {
        TimeSpan startup = phases.GetValueOrDefault(TimedPhase.Startup);
        TimeSpan total = startup + Stopwatch.GetElapsedTime(startedAt);
        foreach (string line in Format(phases, total, Cache)) error.WriteLine(line);
    }

    /// <summary>
    ///     The report's lines: a header, one line per recorded phase in <see cref="TimedPhase" /> order with the
    ///     phases that never ran left out, the total, and the cache verdict where there is one.
    /// </summary>
    // Must agree with .github/scripts/perf_ratios.py, which parses these lines.
    internal static IReadOnlyList<string> Format(
        IReadOnlyDictionary<TimedPhase, TimeSpan> recorded, TimeSpan total, CacheVerdict? cache)
    {
        List<string> lines = ["timings (wall clock, seconds):"];
        IEnumerable<TimedPhase> ran = Enum.GetValues<TimedPhase>()
            .Where(recorded.ContainsKey);
        lines.AddRange(ran.Select(phase => Row(NameOf(phase), recorded[phase])));
        lines.Add(Row("total", total));

        if (cache is { } verdict)
        {
            string outcome = verdict.Outcome.ToString()
                .ToLowerInvariant();
            lines.Add($"cache: {outcome}, {verdict.Extracted} of {verdict.Projects} projects extracted");
        }

        return lines;
    }

    private static string Row(string name, TimeSpan elapsed)
    {
        var seconds = elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture);
        return $"  {name,-15}{seconds,6}";
    }

    private static string NameOf(TimedPhase phase)
    {
        return phase switch
        {
            TimedPhase.Startup => "startup",
            TimedPhase.Resolve => "resolve",
            TimedPhase.MsBuild => "msbuild",
            TimedPhase.CacheRead => "cache read",
            TimedPhase.WorkspaceLoad => "workspace load",
            TimedPhase.SpecResolve => "spec resolve",
            TimedPhase.SpecLoad => "spec load",
            TimedPhase.Extraction => "extraction",
            TimedPhase.CacheWrite => "cache write",
            TimedPhase.Check => "check",
            TimedPhase.Report => "report",
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null)
        };
    }

    // Null where the platform will not say when the process started, which leaves startup out of the report.
    private static TimeSpan? ProcessAge()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return DateTime.Now - process.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return null;
        }
    }

    private Span Open(TimedPhase phase)
    {
        var span = new Span(this, phase, open);
        open = span;
        return span;
    }

    private void Close(Span span)
    {
        TimeSpan elapsed = Stopwatch.GetElapsedTime(span.StartedAt);
        TimeSpan exclusive = elapsed - span.Children;
        phases[span.Phase] = phases.GetValueOrDefault(span.Phase) + exclusive;
        span.Parent?.AddChild(elapsed);
        open = span.Parent;
    }

    private sealed class Span(PhaseTimings owner, TimedPhase phase, Span? parent) : IDisposable
    {
        private bool closed;

        public long StartedAt { get; } = Stopwatch.GetTimestamp();
        public TimedPhase Phase { get; } = phase;
        public Span? Parent { get; } = parent;
        public TimeSpan Children { get; private set; }

        public void AddChild(TimeSpan elapsed)
        {
            Children += elapsed;
        }

        public void Dispose()
        {
            if (closed) return;

            closed = true;
            owner.Close(this);
        }
    }

    private sealed class NoSpan : IDisposable
    {
        public static readonly NoSpan Instance = new();

        public void Dispose()
        {
        }
    }
}
