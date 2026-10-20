using SlideRule.Baselines;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>
///     What checking one rule produced. <see cref="Violations" /> and <see cref="Grandfathered" /> are each in
///     report order, the same on every run, so two runs over unchanged code list them identically.
/// </summary>
internal sealed class RuleResult
{
    private static readonly IReadOnlyDictionary<Violation, BaselineEntry> NothingGrown =
        new Dictionary<Violation, BaselineEntry>();

    // The subject coverage and the ratchet measure arrive whole rather than as loose ints: they land next to each
    // other, and same-typed positionals in a row are a transposition no compiler can catch.
    internal RuleResult(
        ArchRule rule,
        RuleStatus status,
        IReadOnlyList<Violation> violations,
        IReadOnlyList<CheckWarning>? warnings = null,
        string? skipReason = null,
        IReadOnlyList<Violation>? grandfathered = null,
        RatchetMeasure ratchet = default,
        bool baselineCaptured = false,
        IReadOnlyList<BaselineEntry>? grandfatheredEntries = null,
        SubjectCoverage coverage = default,
        IReadOnlyDictionary<Violation, BaselineEntry>? grownEntries = null,
        IReadOnlyList<TypeNode>? exemplar = null)
    {
        Rule = rule;
        Status = status;
        Violations = violations;
        Warnings = warnings ?? Array.Empty<CheckWarning>();
        SkipReason = skipReason;
        Grandfathered = grandfathered ?? Array.Empty<Violation>();
        GrandfatheredSiteCount = Grandfathered.Sum(violation => violation.Sites.Count);
        RatchetCells = RatchetCellSplit.Of(rule, Grandfathered);
        StaleBaselineEntries = ratchet.Stale;
        ShrunkBaselineEntries = ratchet.Shrunk;
        UncountedBaselineEntries = ratchet.Uncounted;
        BaselineCaptured = baselineCaptured;
        GrandfatheredEntries = grandfatheredEntries ?? Array.Empty<BaselineEntry>();
        SubjectTypes = coverage.Types;
        SubjectGeneratedTypes = coverage.Generated;
        GrownEntries = grownEntries ?? NothingGrown;
        Exemplar = exemplar ?? Array.Empty<TypeNode>();
        List<CheckWarning> deadOperands = Warnings
            .Where(warning => warning.Kind == CheckWarningKind.DeadOperand)
            .ToList();
        List<CheckWarning> untested = deadOperands
            .Where(warning => !warning.Role.IsStricter())
            .ToList();
        List<CheckWarning> stricter = deadOperands
            .Where(warning => warning.Role.IsStricter())
            .ToList();
        UntestedOperands = untested
            .Select(warning => warning.Key!)
            .ToList();
        StricterOperands = stricter
            .Select(warning => warning.Key!)
            .ToList();
        UntestedRole = untested
            .Select(warning => warning.Role)
            .FirstOrDefault();
        StricterRole = stricter
            .Select(warning => warning.Role)
            .FirstOrDefault();
    }

    /// <summary>Gets the rule that was checked.</summary>
    public ArchRule Rule { get; }

    /// <summary>Gets the outcome: passed, failed, or skipped for want of a verdict.</summary>
    public RuleStatus Status { get; }

    /// <summary>
    ///     Gets the violations that failed the rule, in report order; empty unless <see cref="Status" /> is
    ///     <see cref="RuleStatus.Failed" />. The ones a baseline tolerated are in <see cref="Grandfathered" />
    ///     instead, but a tolerated pair grown past its entry's site count is here (<see cref="GrownEntries" />).
    /// </summary>
    public IReadOnlyList<Violation> Violations { get; }

    /// <summary>Gets the warnings raised while checking the rule.</summary>
    public IReadOnlyList<CheckWarning> Warnings { get; }

    /// <summary>
    ///     Gets why the run reached no verdict for this rule, written to be printed as it stands; null unless
    ///     <see cref="Status" /> is <see cref="RuleStatus.Skipped" />.
    /// </summary>
    public string? SkipReason { get; }

    /// <summary>
    ///     Gets the violations the rule's captured baseline tolerates: each is reported, in report order,
    ///     and none of them fails the rule — the debt still to be paid down. Empty for a rule that takes no
    ///     baseline, and for one whose baseline has yet to be captured, since until it is every violation
    ///     fails.
    /// </summary>
    public IReadOnlyList<Violation> Grandfathered { get; }

    /// <summary>
    ///     Gets how many source sites <see cref="Grandfathered" />'s violations carry between them. One
    ///     violation covers every site of the same pair, so this is at least
    ///     <see cref="Grandfathered" />'s own count and is the finer measure of the same remaining debt;
    ///     it is available whether or not any baseline entry has a recorded count of its own.
    /// </summary>
    public int GrandfatheredSiteCount { get; }

    /// <summary>
    ///     Gets this rule's remaining tolerated debt grouped by the cell each violation falls in, or
    ///     null when there is nothing to group: the rule takes no baseline, its subject is not a family,
    ///     or nothing is left to burn down.
    /// </summary>
    // Both status channels read this one property — the human row's sub-line and the burndown
    // document's cells array — so a reader cannot find the two disagreeing about the same run.
    public RatchetCellSplit? RatchetCells { get; }

    /// <summary>
    ///     Gets how many entries in the rule's captured baseline no current violation matched. Where the run
    ///     tested the rule's unmatched entries (<see cref="UnmatchedEntriesTested" />) that is debt since fixed,
    ///     which <c>sliderule baseline accept-reductions</c> retires; where it did not, any of them may have
    ///     gone untested rather than been fixed, and that command skips the rule. Matching is on identity alone,
    ///     so a tolerated pair that has grown is matched and live rather than stale, even though it now fails
    ///     the rule. Zero for a rule that takes no baseline.
    /// </summary>
    public int StaleBaselineEntries { get; }

    /// <summary>
    ///     Gets how many matched baseline entries now cover fewer sites than they record — a real reduction.
    ///     Each still passes, and <c>sliderule baseline accept-reductions</c> lowers the recorded count.
    /// </summary>
    public int ShrunkBaselineEntries { get; }

    /// <summary>
    ///     Gets how many matched baseline entries record no site count, so each tolerates its pair however
    ///     many sites it grows to, until a capture records one. Entries that key a subject rather than a
    ///     pair never count here: they carry no site measure at all, so nothing an author could do would
    ///     clear them from this total.
    /// </summary>
    public int UncountedBaselineEntries { get; }

    /// <summary>
    ///     Gets, for each violation that has grown, the baseline entry it went past: a tolerated pair now
    ///     carrying more sites than its entry records, which fails the rule and so appears in
    ///     <see cref="Violations" /> rather than in <see cref="Grandfathered" />, while still naming the
    ///     allowance it exceeded. Look one up with the very instance taken from <see cref="Violations" /> —
    ///     the keys are those instances, compared by reference. Empty when nothing grew.
    /// </summary>
    // Reference-keyed because Violation overrides neither Equals nor GetHashCode, so the default comparer
    // is reference identity — which is what a lookup from a rendered violation needs.
    public IReadOnlyDictionary<Violation, BaselineEntry> GrownEntries { get; }

    /// <summary>Gets how many violations have grown past their baseline entry.</summary>
    public int GrownBaselineEntries => GrownEntries.Count;

    /// <summary>
    ///     Gets whether a baseline has been captured for this rule; false for a rule that takes no baseline.
    /// </summary>
    public bool BaselineCaptured { get; }

    /// <summary>
    ///     Gets the baseline entries that tolerated <see cref="Grandfathered" />, index-aligned with it: the entry
    ///     at index <c>i</c> blessed the violation at index <c>i</c>, and carries the reason recorded with it
    ///     (<see cref="BaselineEntry.Because" />) if it has one.
    /// </summary>
    public IReadOnlyList<BaselineEntry> GrandfatheredEntries { get; }

    /// <summary>
    ///     Gets how many types the rule's subject actually matched. A rule over members counts the types
    ///     that declare them, so this and <see cref="SubjectGeneratedTypes" /> share a unit. Zero for a rule
    ///     that reached no subject at all: one that errored, one the run reached no verdict for, a scope's
    ///     tripwire, or a subject that matched nothing.
    /// </summary>
    public int SubjectTypes { get; }

    /// <summary>
    ///     Gets how many of <see cref="SubjectTypes" /> a generator emitted
    ///     (<see cref="ITypeInfo.IsGenerated" />) — the rule's own answer to whether it is aimed at code
    ///     anyone can act on.
    /// </summary>
    public int SubjectGeneratedTypes { get; }

    /// <summary>
    ///     Gets whether the rule's debt has burned to zero on a run that actually measured it, so its
    ///     posture could move from <see cref="Posture.Migrate" /> to <see cref="Posture.Enforce" />: a
    ///     captured baseline with nothing tolerated, nothing failing and nothing awaiting acceptance. False for
    ///     every other posture, for a rule the run reached no verdict for, and for a rule the run did not measure
    ///     as written (<see cref="MeasuredInFull" />).
    /// </summary>
    // The Skipped conjunct is essential: a narrowing skip keeps BaselineCaptured truthful and zeroes
    // the counts, which is burned-to-zero's exact shape, so without it this would suggest enforcing a
    // rule whose subject a solution filter had erased. MeasuredInFull is the same guard against
    // the same shape arrived at two more ways: a rule whose target rotted passes with nothing tolerated,
    // nothing red and nothing stale, and enforcing it would pin a rule that measures nothing; a rule one of
    // whose target patterns rotted can reach those counts too, and enforcing it would pin a ban missing a
    // clause. A rule stricter than written reaches them only once nothing its dead pattern meant to allow is
    // left, and enforcing it would still pin a pattern its author has to fix or remove.
    public bool Promotable =>
        Rule.Posture == Posture.Migrate
        && Status != RuleStatus.Skipped
        && BaselineCaptured
        && Grandfathered.Count == 0
        && Violations.Count == 0
        && StaleBaselineEntries == 0
        && MeasuredInFull;

    /// <summary>
    ///     Gets whether this verdict rests on a selection that matched nothing, so the rule was reported
    ///     without ever being measured against the code: its subject matched no types
    ///     (<see cref="ViolationKind.EmptySubject" />, which fails it), or its whole forbidden target did
    ///     (<see cref="CheckWarningKind.InertTarget" />, which leaves it passing). Either way every count on
    ///     this result is a count of nothing, and a baseline entry no violation matched went unmatched for
    ///     want of a measurement rather than because the debt was paid: repair the rule's selection, and do
    ///     not accept the reduction. A <see cref="CheckWarningKind.DeadOperand" /> warning does not
    ///     set this, because the rule's other operands were measured.
    /// </summary>
    // The one predicate both wholly rotted shapes answer, so no reader tests the violations for one shape
    // and the warnings for the other and misses whichever it forgot. Distinct from ArchChecker's private
    // SelectedNothing, which asks the narrower question of whether EVERY violation is an empty subject: that
    // one decides a skip, and one real finding disqualifies it.
    public bool SelectionMatchedNothing =>
        Violations.Any(violation => violation.Kind == ViolationKind.EmptySubject)
        || Warnings.Any(warning => warning.Kind == CheckWarningKind.InertTarget);

    /// <summary>
    ///     The solution-declared types the rule's exemplar resolved to, ordered ordinally by full name: the
    ///     types a fix is pointed at, each carrying its declaration sites. Empty when the rule names no
    ///     exemplar, when nothing it names resolved, when the exemplar could not be evaluated, and on a run
    ///     over one project's compilation, which never resolves one.
    /// </summary>
    internal IReadOnlyList<TypeNode> Exemplar { get; }

    /// <summary>
    ///     The dead patterns whose death may hide a violation — target patterns and <c>among:</c> parts — each
    ///     as its <see cref="CheckWarningKind.DeadOperand" /> warning quotes it and in operand order;
    ///     empty when none went dead.
    /// </summary>
    internal IReadOnlyList<string> UntestedOperands { get; }

    /// <summary>
    ///     The dead patterns that only made the rule stricter than written — allow-list operands and
    ///     memberships — each as its <see cref="CheckWarningKind.DeadOperand" /> warning quotes it and in
    ///     operand order; empty when none went dead.
    /// </summary>
    internal IReadOnlyList<string> StricterOperands { get; }

    /// <summary>The role of the first pattern in <see cref="UntestedOperands" />; none when it is empty.</summary>
    internal DeadOperandRole UntestedRole { get; }

    /// <summary>The role of the first pattern in <see cref="StricterOperands" />; none when it is empty.</summary>
    internal DeadOperandRole StricterRole { get; }

    /// <summary>
    ///     Whether the run tested every baseline entry it left unmatched, so each is debt since fixed: neither
    ///     the rule's selection matched nothing (<see cref="SelectionMatchedNothing" />) nor a pattern whose
    ///     death may hide a violation did (<see cref="UntestedOperands" />).
    /// </summary>
    // A dead pattern that only made the rule stricter leaves this true: it adds violations and never
    // unmatches an entry.
    internal bool UnmatchedEntriesTested => !SelectionMatchedNothing && UntestedOperands.Count == 0;

    /// <summary>
    ///     Whether the run measured the rule as written: <see cref="UnmatchedEntriesTested" />, and no pattern
    ///     made it stricter than written either (<see cref="StricterOperands" />).
    /// </summary>
    // It answers for the whole rule because nothing on the result says which of its violations a dead
    // operand accounts for.
    internal bool MeasuredInFull => UnmatchedEntriesTested && StricterOperands.Count == 0;

    /// <summary>
    ///     Whether a baseline capture would grandfather anything of this rule's: any violation carries an identity.
    /// </summary>
    // Asked of the identity rather than of the kinds that lack one: a violation with no baseline identity cannot be
    // recorded, so a hint to capture it would be an instruction that silently does nothing.
    private bool HoldsBaselinableViolations =>
        Violations.Any(violation => violation.BaselineIdentity() is not null);

    /// <summary>
    ///     Whether this rule is waiting on its first baseline capture: it failed, no baseline is captured, and a
    ///     capture would grandfather something.
    /// </summary>
    // One owner, so the checker deciding whether to raise the orphan warning and the renderer deciding whether to
    // print the init hint ask the same question.
    internal bool AwaitsFirstCapture =>
        Status == RuleStatus.Failed && !BaselineCaptured && HoldsBaselinableViolations;

    /// <summary>
    ///     A copy of this result with <paramref name="appended" /> after its own warnings, every other field carried.
    /// </summary>
    internal RuleResult WithWarnings(IReadOnlyList<CheckWarning> appended)
    {
        return WithExemplar(Exemplar, appended);
    }

    /// <summary>
    ///     A copy of this result carrying <paramref name="exemplar" /> as its resolved exemplar types, with
    ///     <paramref name="warnings" /> after its own warnings, every other field carried.
    /// </summary>
    internal RuleResult WithExemplar(IReadOnlyList<TypeNode> exemplar, IReadOnlyList<CheckWarning> warnings)
    {
        return new RuleResult(
            Rule,
            Status,
            Violations,
            Warnings.Concat(warnings).ToList(),
            SkipReason,
            Grandfathered,
            new RatchetMeasure(StaleBaselineEntries, ShrunkBaselineEntries, UncountedBaselineEntries),
            BaselineCaptured,
            GrandfatheredEntries,
            new SubjectCoverage(SubjectTypes, SubjectGeneratedTypes),
            GrownEntries,
            exemplar);
    }
}
