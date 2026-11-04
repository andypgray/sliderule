using Shouldly;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     Assertions over <see cref="ProjectLoadReport" /> — the load's answers, missing, failed and unchecked,
///     asserted together so none can be pinned while another drifts.
/// </summary>
/// <remarks>
///     Every message carries the whole report, every list, because the defect this type exists to prevent is
///     reading one list and forgetting the others: a message quoting only the list asserted on would
///     hide the one that moved. Deliberately not attributed <c>[ShouldlyMethods]</c> — leaving it off is what
///     makes an inner failure name the list under test rather than this file.
/// </remarks>
internal static class ProjectLoadReportAssertions
{
    /// <summary>Asserts the load blamed nothing and left nothing out — the whole solution was checked.</summary>
    internal static void ShouldHaveLoadedEverything(this ProjectLoadReport report)
    {
        report.ShouldHaveReported();
    }

    /// <summary>
    ///     Asserts exactly <paramref name="expected" /> failed to load and nothing was missing or narrowed away —
    ///     the shape of an unfiltered solution that loaded partially.
    /// </summary>
    internal static void ShouldHaveFailed(this ProjectLoadReport report, params string[] expected)
    {
        report.ShouldHaveReported(failed: expected);
    }

    /// <summary>
    ///     Asserts exactly <paramref name="expected" /> are declared and not on disk, and nothing failed or was
    ///     narrowed away.
    /// </summary>
    internal static void ShouldHaveMissing(this ProjectLoadReport report, params string[] expected)
    {
        report.ShouldHaveReported(missing: expected);
    }

    /// <summary>
    ///     Asserts exactly <paramref name="expected" /> were left unchecked and nothing failed — the shape of
    ///     a filtered run that loaded cleanly over a narrowed universe.
    /// </summary>
    internal static void ShouldHaveLeftUnchecked(this ProjectLoadReport report, params string[] expected)
    {
        report.ShouldHaveReported(@unchecked: expected);
    }

    /// <summary>
    ///     Asserts each of the three lists is exactly what is given for it, and a list not given is empty.
    /// </summary>
    internal static void ShouldHaveReported(
        this ProjectLoadReport report, string[]? missing = null, string[]? failed = null, string[]? @unchecked = null)
    {
        string whole = Describe(report);

        report.Missing.ShouldBe(missing ?? [], whole);
        report.Failed.ShouldBe(failed ?? [], whole);
        report.Unchecked.ShouldBe(@unchecked ?? [], whole);
    }

    private static string Describe(ProjectLoadReport report)
    {
        return $"missing: [{string.Join(", ", report.Missing)}]\n"
               + $"failed: [{string.Join(", ", report.Failed)}]\n"
               + $"unchecked: [{string.Join(", ", report.Unchecked)}]";
    }
}
