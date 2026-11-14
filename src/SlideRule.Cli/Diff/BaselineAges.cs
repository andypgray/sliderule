using SlideRule.Baselines;

namespace SlideRule.Cli.Diff;

/// <summary>
///     When each baseline entry at HEAD was recorded and by whom, as <see cref="BaselineHistory" /> read it, keyed by
///     rule ID and entry identity.
/// </summary>
internal sealed class BaselineAges
{
    private readonly IReadOnlyDictionary<EntryKey, Recording> _recordings;
    private readonly bool _shallow;
    private readonly AgeUnknownReason? _unavailable;

    private BaselineAges(
        IReadOnlyDictionary<EntryKey, Recording> recordings, bool shallow, AgeUnknownReason? unavailable)
    {
        _recordings = recordings;
        _shallow = shallow;
        _unavailable = unavailable;
    }

    /// <summary>The answer where git could give none: every entry's age is unknown, for want of history.</summary>
    internal static BaselineAges Unavailable { get; } =
        new(new Dictionary<EntryKey, Recording>(), shallow: false, AgeUnknownReason.NoHistory);

    /// <summary>
    ///     The ages of a replayed history. In a <paramref name="shallow" /> clone, an entry first seen in a commit
    ///     whose parents were not fetched may be older than that commit, so its age is unknown.
    /// </summary>
    internal static BaselineAges Of(IReadOnlyDictionary<EntryKey, Recording> recordings, bool shallow)
    {
        return new BaselineAges(recordings, shallow, unavailable: null);
    }

    /// <summary>
    ///     <paramref name="entries" /> filed under <paramref name="ruleId" />, each with its recording or why it has
    ///     none: the dated ones oldest first, then the undated ones, each run in identity order.
    /// </summary>
    internal IReadOnlyList<DatedEntry> Date(string ruleId, IEnumerable<BaselineEntry> entries)
    {
        return entries
            .Distinct()
            .Select(entry => Dated(ruleId, entry))
            .OrderBy(dated => dated.Recorded is null)
            .ThenBy(dated => dated.Recorded?.RecordedAt)
            .ThenBy(dated => dated.Entry.Source, StringComparer.Ordinal)
            .ThenBy(dated => dated.Entry.Target, StringComparer.Ordinal)
            .ThenBy(dated => dated.Entry.Subject, StringComparer.Ordinal)
            .ToList();
    }

    private DatedEntry Dated(string ruleId, BaselineEntry entry)
    {
        if (_unavailable is { } reason) return new DatedEntry(entry, null, reason);

        // An entry HEAD's history never held is in the working tree only.
        if (!_recordings.TryGetValue(new EntryKey(ruleId, entry), out Recording? recording))
            return new DatedEntry(entry, null, AgeUnknownReason.NotCommitted);

        return _shallow && recording.AtHistoryStart
            ? new DatedEntry(entry, null, AgeUnknownReason.ShallowClone)
            : new DatedEntry(entry, recording.Record, null);
    }

    /// <summary>
    ///     A baseline entry's lineage key: the rule section it is filed under and its identity
    ///     (<see cref="BaselineEntry" /> equality ignores its count and reason).
    /// </summary>
    internal readonly record struct EntryKey(string RuleId, BaselineEntry Identity);

    /// <summary>
    ///     The commit that first recorded an entry's current run, and whether that commit has no parent in the
    ///     history read, which in a shallow clone means its parents were not fetched.
    /// </summary>
    internal sealed record Recording(EntryRecord Record, bool AtHistoryStart);
}

/// <summary>
///     When an entry was recorded, as the committer date of the commit that added it, and that commit's author.
/// </summary>
internal sealed record EntryRecord(DateTimeOffset RecordedAt, string RecordedBy);

/// <summary>An entry with its <see cref="EntryRecord" />, or the reason its age is unknown.</summary>
internal sealed record DatedEntry(BaselineEntry Entry, EntryRecord? Recorded, AgeUnknownReason? Unknown);

/// <summary>Why an entry's age is unknown.</summary>
internal enum AgeUnknownReason
{
    /// <summary>git is missing or timed out, or the solution is not in a repository with a commit.</summary>
    NoHistory,

    /// <summary>The entry is in a shallow clone's oldest fetched commit, so it may be older.</summary>
    ShallowClone,

    /// <summary>The entry is in the working tree and not in HEAD's history.</summary>
    NotCommitted
}
