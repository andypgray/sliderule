using Shouldly;
using SlideRule.Baselines;
using SlideRule.Cli.Diff;

namespace SlideRule.Tests.Cli;

/// <summary>Assertions over the <see cref="BaselineAges" /> a history read returned, one entry at a time.</summary>
/// <remarks>
///     Not attributed <c>[ShouldlyMethods]</c>: each message is built here and names the entry, the rule and what
///     the read said of it, which is the first thing a red needs.
/// </remarks>
internal static class BaselineAgesAssertions
{
    /// <summary>Asserts that <paramref name="entry" /> under <paramref name="ruleId" /> is dated as given.</summary>
    internal static void ShouldDate(
        this BaselineAges ages, string ruleId, BaselineEntry entry, DateTimeOffset recordedAt, string recordedBy)
    {
        DatedEntry dated = ages.ShouldDateOne(ruleId, entry);
        var expected = new EntryRecord(recordedAt, recordedBy);

        dated.Recorded.ShouldBe(expected, $"{Described(ruleId, entry)} read as {Read(dated)}");
        // EntryRecord equality compares instants, so the offset the committer wrote is held separately.
        dated.Recorded!.RecordedAt.Offset.ShouldBe(recordedAt.Offset, $"{Described(ruleId, entry)}'s offset");
    }

    /// <summary>Asserts that <paramref name="entry" /> under <paramref name="ruleId" /> has no date, for the reason given.</summary>
    internal static void ShouldNotDate(
        this BaselineAges ages, string ruleId, BaselineEntry entry, AgeUnknownReason reason)
    {
        DatedEntry dated = ages.ShouldDateOne(ruleId, entry);

        dated.ShouldSatisfyAllConditions(
            $"{Described(ruleId, entry)} read as {Read(dated)}",
            () => dated.Recorded.ShouldBeNull(),
            () => dated.Unknown.ShouldBe(reason));
    }

    private static DatedEntry ShouldDateOne(this BaselineAges ages, string ruleId, BaselineEntry entry)
    {
        return ages.Date(ruleId, [entry])
            .ShouldHaveSingleItem($"dating {Described(ruleId, entry)} alone");
    }

    private static string Described(string ruleId, BaselineEntry entry)
    {
        return $"'{entry.SymbolForm}' under '{ruleId}'";
    }

    private static string Read(DatedEntry dated)
    {
        return dated.Recorded is { } record
            ? $"recorded {record.RecordedAt:O} by {record.RecordedBy}"
            : $"undated ({dated.Unknown})";
    }
}
