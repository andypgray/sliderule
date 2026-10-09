using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>
///     The captured baselines for a whole check run, keyed by rule ID. A rule with a section here has
///     a captured baseline, and the violations its entries identify are grandfathered: reported, but
///     not failing the check. A rule with no section has no captured baseline, and every violation of
///     it fails the check. Only SlideRule's own runners read the baseline files a spec names and build
///     a populated index, so a direct call to <c>ArchChecker.Check</c> passes <see cref="Empty" /> and
///     grandfathers nothing.
/// </summary>
internal sealed class BaselineIndex
{
    private readonly Dictionary<string, RuleBaseline> _sections;

    /// <summary>
    ///     Builds an index over sections already read from baseline files, keyed by rule ID. The sections
    ///     are copied into an ordinal dictionary, so a rule ID is matched ordinally whatever comparer the
    ///     caller's own dictionary carried. A rule whose baseline file has not been captured is simply
    ///     absent from the dictionary.
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
    ///     Gets what a scan of the solution's baseline files found beyond the sections this index holds: the
    ///     sections filed under an ID no rule declares, and the files that could not be read.
    ///     <see cref="BaselineOrphanScan.Empty" /> unless a host that reads the files from disk attached one, so
    ///     an index built inside the compiler never carries a scan.
    /// </summary>
    internal BaselineOrphanScan OrphanScan { get; }

    /// <summary>A copy of this index carrying <paramref name="scan" /> as its <see cref="OrphanScan" />.</summary>
    internal BaselineIndex WithOrphanScan(BaselineOrphanScan scan)
    {
        return new BaselineIndex(_sections, Guard.NotNull(scan, nameof(scan)));
    }

    /// <summary>
    ///     Looks up the captured section for <paramref name="ruleId" />. A rule captured while it had no
    ///     violations is present with zero entries and returns true, which is a different answer from an
    ///     absent rule's: that rule grandfathers nothing because there was nothing to grandfather, where an
    ///     absent rule grandfathers nothing because nothing has been captured for it yet.
    /// </summary>
    /// <param name="ruleId">The rule ID, matched ordinally.</param>
    /// <param name="baseline">The rule's captured section, or null when the rule has none.</param>
    /// <returns><c>true</c> when the rule has a captured baseline, even one with zero entries.</returns>
    public bool TryGet(string ruleId, out RuleBaseline? baseline)
    {
        return _sections.TryGetValue(Guard.NotNull(ruleId, nameof(ruleId)), out baseline);
    }
}
