using SlideRule.Baselines;

namespace SlideRule.Checking;

/// <summary>
///     Everything one check run produced: a <see cref="RuleResult" /> for each rule that ran, and counts rolled
///     up from those results. Every count derives from <see cref="Results" />, so a run narrowed to a few rules
///     is a smaller report of the same shape; <see cref="HasViolations" /> is the verdict.
/// </summary>
internal sealed class CheckReport
{
    internal CheckReport(IReadOnlyList<RuleResult> results, BaselineOrphanScan? orphanScan = null)
    {
        Results = results;
        OrphanScan = orphanScan ?? BaselineOrphanScan.Empty;
        RulesChecked = results.Count;
        RulesPassed = results.Count(r => r.Status == RuleStatus.Passed);
        RulesFailed = results.Count(r => r.Status == RuleStatus.Failed);
        RulesSkipped = results.Count(r => r.Status == RuleStatus.Skipped);
        ViolationCount = results.Sum(r => r.Violations.Count);
        WarningCount = results.Sum(r => r.Warnings.Count);
        GrandfatheredCount = results.Sum(r => r.Grandfathered.Count);
        GrandfatheredSiteCount = results.Sum(r => r.GrandfatheredSiteCount);
        // The two halves of the mechanical unmatched total, partitioned on the one question that decides
        // what an unmatched entry means: did the rule that left it unmatched test it.
        StaleBaselineEntryCount = results.Where(r => r.UnmatchedEntriesTested)
            .Sum(r => r.StaleBaselineEntries);
        UnmeasuredBaselineEntryCount = results.Where(r => !r.UnmatchedEntriesTested)
            .Sum(r => r.StaleBaselineEntries);
        ShrunkBaselineEntryCount = results.Sum(r => r.ShrunkBaselineEntries);
        UncountedBaselineEntryCount = results.Sum(r => r.UncountedBaselineEntries);
    }

    /// <summary>Gets each rule's result, in the order the spec declares the rules.</summary>
    public IReadOnlyList<RuleResult> Results { get; }

    /// <summary>Gets the run's baseline-file scan; empty for a run whose baselines were not read from disk.</summary>
    internal BaselineOrphanScan OrphanScan { get; }

    /// <summary>Gets how many rules the run covered — the size of <see cref="Results" />.</summary>
    public int RulesChecked { get; }

    /// <summary>Gets how many rules passed (<see cref="RuleStatus.Passed" />).</summary>
    public int RulesPassed { get; }

    /// <summary>Gets how many rules failed (<see cref="RuleStatus.Failed" />).</summary>
    public int RulesFailed { get; }

    /// <summary>Gets how many rules the run reached no verdict for (<see cref="RuleStatus.Skipped" />).</summary>
    public int RulesSkipped { get; }

    /// <summary>
    ///     Gets how many violations failed their rule, across every rule. Grandfathered violations are not
    ///     counted here; <see cref="GrandfatheredCount" /> holds those.
    /// </summary>
    public int ViolationCount { get; }

    /// <summary>Gets how many warnings the run raised across every rule.</summary>
    public int WarningCount { get; }

    /// <summary>
    ///     Gets how many violations a baseline tolerated across every rule: the debt still to be paid down.
    /// </summary>
    public int GrandfatheredCount { get; }

    /// <summary>Gets the sum of <see cref="RuleResult.GrandfatheredSiteCount" /> across every rule.</summary>
    public int GrandfatheredSiteCount { get; }

    /// <summary>
    ///     Gets how many recorded baseline entries no current violation matched, across the rules whose
    ///     unmatched entries this run tested (<see cref="RuleResult.UnmatchedEntriesTested" />) — debt that has
    ///     since been fixed. <c>sliderule baseline accept-reductions</c> retires them. An entry left unmatched
    ///     by any other rule is counted by <see cref="UnmeasuredBaselineEntryCount" /> instead, never here: it
    ///     may not be fixed debt, and accepting it could delete a live entry.
    /// </summary>
    public int StaleBaselineEntryCount { get; }

    /// <summary>
    ///     Gets how many recorded baseline entries went unmatched on a rule whose unmatched entries the run
    ///     could not test (<see cref="RuleResult.UnmatchedEntriesTested" /> false), such as one whose subject or
    ///     whole forbidden target now matches no types. The entries may be untouched debt of unknown size, so
    ///     the remedy is the rule's selection rather than a baseline write.
    /// </summary>
    public int UnmeasuredBaselineEntryCount { get; }

    /// <summary>Gets the sum of <see cref="RuleResult.ShrunkBaselineEntries" /> across every rule.</summary>
    public int ShrunkBaselineEntryCount { get; }

    /// <summary>
    ///     Gets the sum of <see cref="RuleResult.UncountedBaselineEntries" /> across every rule;
    ///     <c>sliderule baseline accept-reductions</c> records a count.
    /// </summary>
    public int UncountedBaselineEntryCount { get; }

    /// <summary>
    ///     Gets whether any rule failed, the run's verdict; a run whose every violation is grandfathered is clean.
    /// </summary>
    public bool HasViolations => RulesFailed > 0;
}
