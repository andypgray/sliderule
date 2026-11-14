using Shouldly;
using SlideRule.Cli.Pipeline;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>The <c>check --timings</c> block's text and its exclusive accounting of nested phases.</summary>
public sealed class PhaseTimingsTests
{
    [Fact]
    public void Format_ListsRecordedPhasesInReportOrderThenTheTotalAndTheCacheLine()
    {
        // Recorded out of order and without a workspace load, as a cache hit records them.
        var recorded = new Dictionary<TimedPhase, TimeSpan>
        {
            [TimedPhase.Report] = TimeSpan.FromMilliseconds(30),
            [TimedPhase.Extraction] = TimeSpan.FromMilliseconds(31_700),
            [TimedPhase.Startup] = TimeSpan.FromMilliseconds(410),
            [TimedPhase.CacheRead] = TimeSpan.FromMilliseconds(50),
            [TimedPhase.SpecLoad] = TimeSpan.FromMilliseconds(220),
            [TimedPhase.Check] = TimeSpan.FromMilliseconds(400)
        };

        IReadOnlyList<string> lines = PhaseTimings.Format(
            recorded, TimeSpan.FromMilliseconds(144_080), new CacheVerdict(CodebaseSourceOutcome.Hit, 0, 13));

        lines.ShouldBe(
        [
            "timings (wall clock, seconds):",
            "  startup          0.41",
            "  cache read       0.05",
            "  spec load        0.22",
            "  extraction      31.70",
            "  check            0.40",
            "  report           0.03",
            "  total          144.08",
            "cache: hit, 0 of 13 projects extracted"
        ]);
    }

    [Fact]
    public void Format_WithoutACacheVerdict_EndsAtTheTotal()
    {
        var recorded = new Dictionary<TimedPhase, TimeSpan> { [TimedPhase.Resolve] = TimeSpan.FromMilliseconds(20) };

        IReadOnlyList<string> lines = PhaseTimings.Format(recorded, TimeSpan.FromMilliseconds(20), null);

        lines.ShouldBe(["timings (wall clock, seconds):", "  resolve          0.02", "  total            0.02"]);
    }

    [Fact]
    public void Measure_NestedInsideAnother_IsSubtractedFromItsParent()
    {
        using PhaseTimings timings = PhaseTimings.Begin();
        using (PhaseTimings.Measure(TimedPhase.Extraction))
        using (PhaseTimings.Measure(TimedPhase.CacheWrite))
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(300));
        }

        var error = new StringWriter();
        timings.WriteTo(error);

        TimingsBlock block = error.ToString()
            .ShouldBeTimings();
        block.Rows.ShouldContain(row => row.Name == "cache write" && row.Seconds >= 0.30, error.ToString());
        block.Rows.ShouldContain(row => row.Name == "extraction" && row.Seconds < 0.15, error.ToString());
    }
}
