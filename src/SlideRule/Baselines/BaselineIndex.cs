using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>
///     The captured baselines for a whole check run, keyed by rule ID. A rule with a section here has
///     a captured baseline, and the violations its entries identify are grandfathered: reported, but
///     not failing the check. A rule with no section has no captured baseline, and every violation of
///     it fails the check. A section with zero entries is still a capture, one that found nothing to
///     grandfather.
/// </summary>
internal sealed class BaselineIndex
{
    private readonly Dictionary<string, RuleBaseline> _sections;

    /// <summary>
    ///     Copies <paramref name="sections" /> into an ordinal dictionary, so a rule ID is matched ordinally
    ///     whatever comparer the caller's own dictionary carried.
    /// </summary>
    internal BaselineIndex(IReadOnlyDictionary<string, RuleBaseline> sections)
    {
        IReadOnlyDictionary<string, RuleBaseline> given = Guard.NotNull(sections, nameof(sections));

        _sections = given.ToDictionary(section => section.Key, section => section.Value, StringComparer.Ordinal);
        OrphanScan = BaselineOrphanScan.Empty;
    }

    private BaselineIndex(Dictionary<string, RuleBaseline> sections, BaselineOrphanScan orphanScan)
    {
        _sections = sections;
        OrphanScan = orphanScan;
    }

    /// <summary>Gets the index in which no rule has a captured baseline, so every violation fails the check.</summary>
    public static BaselineIndex Empty { get; } = new(new Dictionary<string, RuleBaseline>());

    /// <summary>
    ///     Gets what a scan of the solution's baseline files found beyond the sections this index holds:
    ///     <see cref="BaselineOrphanScan.Empty" /> unless a host that reads the files from disk attached one, so
    ///     an index built inside the compiler never carries a scan.
    /// </summary>
    internal BaselineOrphanScan OrphanScan { get; }

    internal BaselineIndex WithOrphanScan(BaselineOrphanScan scan)
    {
        return new BaselineIndex(_sections, Guard.NotNull(scan, nameof(scan)));
    }

    /// <summary>
    ///     Looks up the captured section for <paramref name="ruleId" />, matched ordinally: true for a rule
    ///     captured with zero entries too.
    /// </summary>
    public bool TryGet(string ruleId, out RuleBaseline? baseline)
    {
        return _sections.TryGetValue(Guard.NotNull(ruleId, nameof(ruleId)), out baseline);
    }
}
