using SlideRule.Baselines;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>
///     Checks a spec's rules against an extracted codebase and returns a <see cref="CheckReport" /> holding one
///     <see cref="RuleResult" /> per rule, in the order the rules arrive, which for a whole model is the order
///     the spec declares them. It builds nothing, restores nothing and reads nothing from disk: everything it
///     judges arrives in the arguments, so a codebase extracted before the last edit yields a verdict about the
///     code as it then stood. No argument may be null except where a signature marks it nullable.
/// </summary>
/// <remarks>
///     A rule that cannot be evaluated at all (a <see cref="RuleEvaluationException" />, or an unexpected
///     fault) comes back as a failed result carrying the message rather than as a thrown exception, so one
///     broken rule never costs the rest of the run. An <c>Enforce</c> rule fails on any violation. A
///     <c>Migrate</c> rule and a quarantine's <c>containment</c> rule are evaluated the same way and then split
///     against the baselines: a violation whose <see cref="Violation.BaselineIdentity" /> a baseline records is
///     grandfathered, unless its pair now carries more sites than that entry recorded, and every other one
///     fails. A scope's <c>tripwire</c> warns once per changed file that declares a type inside the scope and
///     passes, or is skipped when no <see cref="DiffContext" /> was given.
/// </remarks>
internal static class ArchChecker
{
    /// <summary>Pinned skip reason for a Quarantine tripwire when no <c>--diff-base</c> diff context is present.</summary>
    internal const string TripwireSkipReason =
        "Tripwire: no diff context — run 'sliderule check --diff-base <ref>' to check changed files against this quarantined scope.";

    /// <summary>Pinned skip reason for a Caution tripwire when no <c>--diff-base</c> diff context is present.</summary>
    internal const string CautionTripwireSkipReason =
        "Tripwire: no diff context — run 'sliderule check --diff-base <ref>' to check changed files against this cautioned scope.";

    // Stateless, so one instance serves every rule of every run.
    private static readonly IComparer<(string, string, string, string)> ReportOrder = new ReportOrderComparer();

    /// <summary>
    ///     Checks every rule with no baselines and no diff, so every violation fails and every tripwire skips.
    /// </summary>
    public static CheckReport Check(ArchitectureModel model, CodebaseModel codebase)
    {
        return Check(model, codebase, BaselineIndex.Empty, null);
    }

    /// <summary>
    ///     Checks every rule against <paramref name="baselines" /> with no diff, so every tripwire skips;
    ///     <see cref="BaselineIndex.Empty" /> grandfathers nothing.
    /// </summary>
    public static CheckReport Check(ArchitectureModel model, CodebaseModel codebase, BaselineIndex baselines)
    {
        return Check(model, codebase, baselines, null);
    }

    /// <summary>
    ///     Checks every rule against <paramref name="baselines" /> and the changed files in
    ///     <paramref name="diff" />, or skips every tripwire where <paramref name="diff" /> is null.
    /// </summary>
    public static CheckReport Check(
        ArchitectureModel model, CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff)
    {
        Guard.NotNull(model, nameof(model));

        return Check(model.Rules, codebase, baselines, diff);
    }

    /// <summary>
    ///     Checks exactly <paramref name="rules" />, the whole model's or the subset <see cref="SelectRules" />
    ///     chose, and reports on those alone; choosing a subset changes nothing about how a rule is judged.
    /// </summary>
    public static CheckReport Check(
        IReadOnlyList<ArchRule> rules, CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff)
    {
        return Check(rules, codebase, baselines, diff, null, null);
    }

    /// <summary>
    ///     Checks exactly <paramref name="rules" /> over a run whose universe may be smaller than the solution
    ///     (<paramref name="narrowing" />) or whose model may be smaller than the codebase
    ///     (<paramref name="incompleteModel" />, the fact that part of it never loaded); each is null where the
    ///     universe or the model is whole. <paramref name="root" /> is the directory path globs are relative to,
    ///     or null for a run that has none to give.
    /// </summary>
    /// <remarks>
    ///     Under a narrowing, a rule whose every violation is an empty subject is <see cref="RuleStatus.Skipped" />
    ///     rather than red, because a subject that lives wholly in the projects the run left out is the narrowing's
    ///     doing and not a defect in the spec, and a rule whose target, or one of whose target patterns, matched
    ///     nothing still warns but names the narrowing as its cure. Under a partial model, a rule whose selection
    ///     matched nothing still fails or warns as it would, but carries that fact as its cure instead of advice
    ///     about the shape it was written in; a partial model skips nothing.
    /// </remarks>
    internal static CheckReport Check(
        IReadOnlyList<ArchRule> rules, CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff,
        NarrowedUniverse? narrowing, IncompleteModel? incompleteModel, SolutionRoot? root = null)
    {
        return Check(rules, codebase, baselines, diff, narrowing, incompleteModel, root, CancellationToken.None);
    }

    /// <summary>
    ///     The same check, abandonable: <paramref name="cancellationToken" /> is observed once before each
    ///     rule is evaluated, so a caller that has given up waits at worst for the rule already running.
    /// </summary>
    /// <remarks>
    ///     A rule is the grain because it is the unit whose result the report carries: a run abandoned
    ///     part-way returns nothing at all rather than a report silently missing its tail. Cancellation
    ///     therefore leaves by being thrown, and the blanket arms in <see cref="Verdict" /> and
    ///     <see cref="AttachExemplar" /> let it past for that reason. The token is per-call, and the two
    ///     evaluators built here hold no static state, so concurrent runs over one model cannot see each
    ///     other's.
    /// </remarks>
    internal static CheckReport Check(
        IReadOnlyList<ArchRule> rules, CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff,
        NarrowedUniverse? narrowing, IncompleteModel? incompleteModel, SolutionRoot? root,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(rules, nameof(rules));
        Guard.NotNull(codebase, nameof(codebase));
        Guard.NotNull(baselines, nameof(baselines));

        // One selection evaluator for the whole run — the constraint arms and the tripwire path share it.
        // Its constructor materializes the solution-declared type list and the noun indexes, so building a
        // second would repeat a full pass over the model; it holds no per-rule mutable state.
        var selections = new SelectionEvaluator(codebase, root);
        var evaluator = new ConstraintEvaluator(codebase, selections, incompleteModel, narrowing);

        // An explicit loop rather than a Select, so the poll sits before the rule it guards; the results
        // stay in the order the rules arrived, which is the order the report declares.
        var results = new List<RuleResult>(rules.Count);
        foreach (ArchRule rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(CheckRule(rule, evaluator, selections, baselines, diff, narrowing));
        }

        return new CheckReport(results, baselines.OrphanScan);
    }

    /// <summary>
    ///     Selects the rules whose ID matches one of <paramref name="ruleIdGlobs" />, in the order the spec declares
    ///     them, before any of the cost of checking is paid; an empty list selects every rule.
    /// </summary>
    /// <remarks>
    ///     A glob is matched against the whole rule ID as one case-sensitive token, where <c>*</c> stands
    ///     for any run of characters, the <c>/</c> separator included. There is no implicit subtree:
    ///     <c>legacy/billing</c> selects a rule with exactly that ID and none of the rules beneath it, while
    ///     <c>legacy/billing/*</c> selects the rules a scope of that ID brings with it
    ///     (<c>legacy/billing/containment</c> and <c>legacy/billing/tripwire</c> for a quarantine, the
    ///     tripwire alone for a caution).
    /// </remarks>
    public static IReadOnlyList<ArchRule> SelectRules(ArchitectureModel model, IReadOnlyList<string> ruleIdGlobs)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNull(ruleIdGlobs, nameof(ruleIdGlobs));

        if (ruleIdGlobs.Count == 0) return model.Rules;

        return model.Rules
            .Where(rule => Wildcard.MatchesAny(ruleIdGlobs, rule.Id))
            .ToList();
    }

    private static RuleResult CheckRule(
        ArchRule rule, ConstraintEvaluator evaluator, SelectionEvaluator selections, BaselineIndex baselines,
        DiffContext? diff, NarrowedUniverse? narrowing)
    {
        RuleResult verdict = Verdict(rule, evaluator, selections, baselines, diff, narrowing);
        return AttachExemplar(verdict, rule, evaluator, narrowing);
    }

    // The exemplar is resolved after the verdict and in a guard of its own, because it never decides one: any
    // fault resolving it, expected or not, is a warning on whatever the rule produced, never a RuleError, which
    // the verdict's shared guard would make it. Cancellation travels on. A run over one project's compilation
    // never resolves it, since most of what an exemplar names lives in other projects and a dead part there is
    // no evidence of anything.
    private static RuleResult AttachExemplar(
        RuleResult verdict, ArchRule rule, ConstraintEvaluator evaluator, NarrowedUniverse? narrowing)
    {
        if (rule.ExemplarSelection is not { } exemplar || narrowing is { Kind: NarrowingKind.OneProject })
            return verdict;

        try
        {
            (IReadOnlyList<TypeNode> types, IReadOnlyList<CheckWarning> warnings) = evaluator.ResolveExemplar(exemplar);
            return verdict.WithExemplar(types, warnings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return verdict.WithWarnings([CheckWarning.ExemplarFault(rule.Exemplar!, ex.Message)]);
        }
    }

    private static RuleResult Verdict(
        ArchRule rule, ConstraintEvaluator evaluator, SelectionEvaluator selections, BaselineIndex baselines,
        DiffContext? diff, NarrowedUniverse? narrowing)
    {
        try
        {
            // The tripwire carries no closed-vocabulary constraint (its Constraint is null and must never reach the
            // evaluator). Inside the try because the class promises every evaluation fault becomes a RuleError rather
            // than aborting the run, and evaluating the scoped selection is an evaluation like any other.
            if (rule.Scope is { Role: ScopeRole.Tripwire }) return Tripwire(rule, selections, diff);

            // The cut is the checker's, never the evaluator's. The evaluator's gates are what make an empty
            // part an authoring error, and they stay exactly that for every run over a whole solution; what a
            // run over one project's compilation may honestly be asked is decided before the evaluator is.
            Selection? subject = rule.Constraint!.Subject;
            if (narrowing is { Kind: NarrowingKind.OneProject } && subject is not null)
                subject = OneProjectSubject.Cut(selections, subject);

            (IReadOnlyList<Violation> violations, IReadOnlyList<CheckWarning> warnings, SubjectCoverage coverage) =
                evaluator.Evaluate(rule.Constraint!, subject);
            // Ahead of the ratchet fork, because the question it answers — did this run have the subject in
            // view at all — is asked of the raw violations and is the same one for both branches.
            if (narrowing is not null && SelectedNothing(violations))
                return NarrowedSkip(rule, warnings, baselines, narrowing);

            return rule.IsRatcheted
                ? Ratchet(rule, violations, warnings, coverage, baselines)
                : Enforce(rule, violations, warnings, coverage);
        }
        catch (RuleEvaluationException ex)
        {
            return Errored(rule, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Defensive: an unexpected fault in one rule must not crash the whole run. Cancellation travels
            // on rather than being held here, because a cancelled run is not a fault in a rule, and
            // reporting it as one would print a rule error for every build a user interrupts.
            return Errored(rule, $"Rule evaluation failed: {ex.Message}");
        }
    }

    private static RuleResult Enforce(
        ArchRule rule, IReadOnlyList<Violation> violations, IReadOnlyList<CheckWarning> warnings, SubjectCoverage coverage)
    {
        IReadOnlyList<Violation> ordered = Order(violations);
        RuleStatus status = ordered.Count > 0 ? RuleStatus.Failed : RuleStatus.Passed;
        return new RuleResult(rule, status, ordered, warnings, coverage: coverage);
    }

    // The ratchet, shared by Migrate and Quarantine containment: a violation whose identity
    // is grandfathered by the rule's captured baseline section passes; everything else — including a new
    // forbidden target from a grandfathered source (pair identity, GRAMMAR §4.3) and every
    // EmptySubject/RuleError (never baselinable) — is red. A matched edge entry may also record how many
    // sites it grandfathers, and then the count decides too: more sites than it records is new code in the
    // old pattern and red, fewer is a reduction that passes and awaits acceptance.
    private static RuleResult Ratchet(
        ArchRule rule, IReadOnlyList<Violation> violations, IReadOnlyList<CheckWarning> warnings,
        SubjectCoverage coverage, BaselineIndex baselines)
    {
        bool captured = baselines.TryGet(rule.Id, out RuleBaseline? section);
        var red = new List<Violation>();
        var grandfatheredPairs = new List<(Violation Violation, BaselineEntry Entry)>();
        var grown = new Dictionary<Violation, BaselineEntry>();
        var matched = new HashSet<BaselineEntry>();
        var shrunk = 0;
        var uncounted = new HashSet<BaselineEntry>();

        foreach (Violation violation in violations)
        {
            BaselineEntry? key = violation.BaselineIdentity();
            // Pair the violation with the STORED entry (not the synthesized key) so the entry's
            // attribution and its recorded count both reach the report.
            if (key is null || section is null || !section.TryMatch(key, out BaselineEntry? stored))
            {
                red.Add(violation);
                continue;
            }

            // Matched keys on identity alone, so the stale count is unchanged whether or not the entry
            // carried a because or a count — and a grown pair, matched here and red below, is never stale:
            // it is live debt that grew, not debt that was fixed and is awaiting acceptance. The stored entry
            // rather than the key, because a project-wide entry standing in for each of the project's items is
            // one entry however many items it matched.
            BaselineEntry entry = stored!;
            matched.Add(entry);

            switch (BaselineRatchet.Classify(key, entry, violation.Sites.Count))
            {
                case RatchetState.Grown:
                    // Red, and into the same list as everything else red, so one comparer orders them all
                    // and a grown pair's sites interleave in report order rather than trailing the block.
                    grown.Add(violation, entry);
                    red.Add(violation);
                    continue;
                case RatchetState.Uncounted:
                    uncounted.Add(entry);
                    break;
                case RatchetState.Shrunk:
                    shrunk++;
                    break;
                case RatchetState.NotMeasured:
                case RatchetState.Held:
                    break;
            }

            grandfatheredPairs.Add((violation, entry));
        }

        IReadOnlyList<Violation> orderedRed = Order(red);
        List<(Violation Violation, BaselineEntry Entry)> orderedGrandfathered = OrderPairs(grandfatheredPairs);

        // Split in one pass so the two lists are index-aligned by construction: Grandfathered[i] is the
        // violation the entry at GrandfatheredEntries[i] blessed.
        var grandfathered = new List<Violation>(orderedGrandfathered.Count);
        var grandfatheredEntries = new List<BaselineEntry>(orderedGrandfathered.Count);
        for (var i = 0; i < orderedGrandfathered.Count; i++)
        {
            grandfathered.Add(orderedGrandfathered[i].Violation);
            grandfatheredEntries.Add(orderedGrandfathered[i].Entry);
        }

        int stale = section is null ? 0 : section.Count - matched.Count;
        RuleStatus status = orderedRed.Count > 0 ? RuleStatus.Failed : RuleStatus.Passed;
        var result = new RuleResult(
            rule, status, orderedRed, warnings, null, grandfathered, new RatchetMeasure(stale, shrunk, uncounted.Count),
            captured, grandfatheredEntries, coverage, grown);

        // Exactly where the report would hint a first capture, a section no rule names is named instead, one
        // warning each so every hint carries its own --from: a rule renamed after capture reads as a rule never
        // captured, and a fresh capture would record as new the debt that section already holds. A rule not
        // measured in full still gets the warning, since a rename measures nothing; its hint just omits init.
        IReadOnlyList<BaselineOrphan> orphans = baselines.OrphanScan.Orphans;
        if (!result.AwaitsFirstCapture || orphans.Count == 0) return result;

        List<CheckWarning> named = orphans
            .Select(orphan => CheckWarning.OrphanedBaseline(rule.Id, orphan, offerInit: result.MeasuredInFull))
            .ToList();
        return result.WithWarnings(named);
    }

    // An empty scoped selection yields zero warnings and passes silently: for a quarantine, containment's
    // EmptySubject is the loud misconfiguration channel; a caution has no such channel, and accepts the
    // silence as the price of having no red state at all.
    private static RuleResult Tripwire(ArchRule rule, SelectionEvaluator selections, DiffContext? diff)
    {
        bool caution = rule.Posture == Posture.Caution;
        if (diff is null) return Skipped(rule, caution ? CautionTripwireSkipReason : TripwireSkipReason);

        string scopeId = rule.Scope!.ScopeId;
        HashSet<TypeNode> scoped = selections.Evaluate(rule.Scope.Scoped, SelectionPosition.Subject);

        List<CheckWarning> touched = scoped
            .Where(type => !type.IsExternal)
            .SelectMany(type => type.DeclarationSites)
            .Select(site => site.FilePath)
            .Distinct(StringComparer.Ordinal)
            .Where(diff.Contains)
            .Select(diff.SolutionRelative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => CheckWarning.ScopeTouched(caution, TripwireMessage(path, scopeId, rule.Id, caution), path))
            .ToList();

        return new RuleResult(rule, RuleStatus.Passed, Array.Empty<Violation>(), touched);
    }

    // The command names the RULE, which is the tripwire's own id, rather than respelling how one is minted from a
    // scope: text the reader is told to paste must not go stale against that mint.
    private static string TripwireMessage(string relativePath, string scopeId, string ruleId, bool caution)
    {
        return caution
            ? $"Changed file '{relativePath}' is inside cautioned scope '{scopeId}' — read the dragons before " +
              $"editing: sliderule explain {ruleId}."
            : $"Changed file '{relativePath}' is inside quarantined scope '{scopeId}' — does the task actually " +
              $"require editing dragon territory? Dragons: sliderule explain {ruleId}.";
    }

    // A rule the narrowed run never had in view: its subject selection matched nothing, and nothing is
    // exactly what a filter that dropped whole projects would produce. Only "every violation is an empty
    // subject" qualifies — one real finding means types did load and the rule was measured.
    private static bool SelectedNothing(IReadOnlyList<Violation> violations)
    {
        return violations.Count > 0 && violations.All(violation => violation.Kind == ViolationKind.EmptySubject);
    }

    // The narrowing skip, minted apart from Skipped() below, which drops warnings and hardcodes an
    // uncaptured baseline. Here the warnings still hold (they are facts about the projects that did load),
    // BaselineCaptured stays truthful — and the ratchet counts are forced to nothing, because Ratchet would
    // otherwise report the whole captured section as "fixed awaiting acceptance" for a rule this run never
    // measured, which is the reduction 'baseline accept-reductions' must never be offered.
    private static RuleResult NarrowedSkip(
        ArchRule rule, IReadOnlyList<CheckWarning> warnings, BaselineIndex baselines, NarrowedUniverse narrowing)
    {
        bool captured = rule.IsRatcheted && baselines.TryGet(rule.Id, out _);
        return new RuleResult(
            rule, RuleStatus.Skipped, Array.Empty<Violation>(), warnings, narrowing.RuleSkipReason,
            baselineCaptured: captured);
    }

    private static RuleResult Skipped(ArchRule rule, string reason)
    {
        return new RuleResult(rule, RuleStatus.Skipped, Array.Empty<Violation>(), skipReason: reason);
    }

    private static RuleResult Errored(ArchRule rule, string detail)
    {
        return new RuleResult(rule, RuleStatus.Failed, [Violation.RuleError(detail)]);
    }

    // Deterministic within-rule order, from the one comparer both lists take.
    private static IReadOnlyList<Violation> Order(IReadOnlyList<Violation> violations)
    {
        return violations
            .OrderBy(violation => violation.OrderKey, ReportOrder)
            .ToList();
    }

    // The ratchet's grandfathered (violation, entry) pairs, ordered by that same comparer so the two split
    // lists — Grandfathered and its index-aligned GrandfatheredEntries — share the order Order produces
    // (OrderBy is a stable sort, so equal keys keep the order they arrived in on both sides).
    private static List<(Violation Violation, BaselineEntry Entry)> OrderPairs(
        List<(Violation Violation, BaselineEntry Entry)> pairs)
    {
        return pairs
            .OrderBy(pair => pair.Violation.OrderKey, ReportOrder)
            .ToList();
    }

    /// <summary>
    ///     The report order every violation list takes: <see cref="Violation.OrderKey" />'s four slots,
    ///     compared ordinal one slot at a time — never as a tuple, whose default string comparison is
    ///     culture-sensitive. The key is what is sorted on rather than the violation, so a sort reads each
    ///     violation's key once instead of once per comparison. One comparer rather than a key chain per
    ///     list, because the red violations and the grandfathered pairs have to come out in the same order
    ///     and nothing else would hold them to it.
    /// </summary>
    private sealed class ReportOrderComparer : IComparer<(string Primary, string Secondary, string Tertiary, string Quaternary)>
    {
        public int Compare(
            (string Primary, string Secondary, string Tertiary, string Quaternary) left,
            (string Primary, string Secondary, string Tertiary, string Quaternary) right)
        {
            int primary = StringComparer.Ordinal.Compare(left.Primary, right.Primary);
            if (primary != 0) return primary;

            int secondary = StringComparer.Ordinal.Compare(left.Secondary, right.Secondary);
            if (secondary != 0) return secondary;

            int tertiary = StringComparer.Ordinal.Compare(left.Tertiary, right.Tertiary);
            return tertiary != 0 ? tertiary : StringComparer.Ordinal.Compare(left.Quaternary, right.Quaternary);
        }
    }
}
