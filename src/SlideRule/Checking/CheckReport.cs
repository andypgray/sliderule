using SlideRule.Baselines;

namespace SlideRule.Checking;

/// <summary>
///     Everything one check run produced: a <see cref="RuleResult" /> for each rule that ran, in the
///     order the spec declares them, and counts rolled up from those results. Returned by the
///     <c>Check</c> methods on <see cref="ArchChecker" />. Every count here derives from
///     <see cref="Results" />, so a run narrowed to a few rules is a smaller report of the same shape.
///     <see cref="HasViolations" /> is the verdict.
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

    /// <summary>
    ///     Gets what the scan of the solution's baseline files found beside the rules' own sections: the sections
    ///     filed under an ID no rule declares, and the files that could not be read. Empty for a run whose
    ///     baselines were not read from disk.
    /// </summary>
    internal BaselineOrphanScan OrphanScan { get; }

    /// <summary>Gets how many rules the run covered — the size of <see cref="Results" />.</summary>
    public int RulesChecked { get; }

    /// <summary>
    ///     Gets how many rules held. A rule that only warned counts here, and so does one whose every
    ///     violation a baseline grandfathered.
    /// </summary>
    public int RulesPassed { get; }

    /// <summary>
    ///     Gets how many rules did not hold: those carrying a violation no baseline grandfathers, those
    ///     whose subject matched nothing, and those that errored while being evaluated.
    /// </summary>
    public int RulesFailed { get; }

    /// <summary>
    ///     Gets how many rules the run reached no verdict for — a scope's tripwire when the check ran with
    ///     no <c>--diff-base</c>, or a rule whose whole subject lies in projects a <c>.slnf</c> solution
    ///     filter left unchecked. Each such result says which, in its <see cref="RuleResult.SkipReason" />.
    /// </summary>
    public int RulesSkipped { get; }

    /// <summary>
    ///     Gets how many violations failed their rule, across every rule. Grandfathered violations are not
    ///     counted here; <see cref="GrandfatheredCount" /> holds those.
    /// </summary>
    public int ViolationCount { get; }

    /// <summary>
    ///     Gets how many warnings the run raised across every rule. A warning fails nothing and never changes the exit
    ///     code.
    /// </summary>
    public int WarningCount { get; }

    /// <summary>
    ///     Gets how many violations a baseline tolerated across every rule — the debt still to be paid down.
    ///     Each is reported and none of them fails its rule.
    /// </summary>
    public int GrandfatheredCount { get; }

    /// <summary>
    ///     Gets how many source sites those grandfathered violations carry between them. One violation
    ///     covers every site of the same pair, so this is at least <see cref="GrandfatheredCount" /> and is
    ///     the finer measure of the same remaining debt.
    /// </summary>
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
    ///     the remedy is the rule's selection rather than a baseline write. Together with
    ///     <see cref="StaleBaselineEntryCount" /> this totals every unmatched entry the run saw.
    /// </summary>
    public int UnmeasuredBaselineEntryCount { get; }

    /// <summary>
    ///     Gets how many matched baseline entries now cover fewer sites than they record, across every rule.
    ///     Each still passes; <c>sliderule baseline accept-reductions</c> lowers the recorded count.
    /// </summary>
    public int ShrunkBaselineEntryCount { get; }

    /// <summary>
    ///     Gets how many matched baseline entries record no site count at all, across every rule, so each
    ///     tolerates its pair however many sites it grows to.
    ///     <c>sliderule baseline accept-reductions</c> records a count.
    /// </summary>
    public int UncountedBaselineEntryCount { get; }

    /// <summary>
    ///     Gets whether any rule failed: the run's verdict, and what makes the CLI's <c>check</c> verb exit
    ///     with 1. Grandfathered violations do not count, so a run whose every violation is grandfathered is
    ///     clean.
    /// </summary>
    public bool HasViolations => RulesFailed > 0;
}
