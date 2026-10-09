using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Internal;
using SlideRule.Model;

namespace SlideRule.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as the plain text <c>sliderule check</c> prints: one block
///     per rule, in the order the spec declares the rules, then a one-line summary.
/// </summary>
/// <remarks>
///     <para>
///         Each block is headed by a status word (<c>pass</c>, <c>warn</c>, <c>FAIL</c> or <c>skip</c>, with
///         no colour codes), the rule ID and the rule's sentence. A failed rule adds its reason, its citation
///         where it has one, its fix and its exemplar where it has them and the run reached a verdict about
///         the code, and a line per violation site as <c>path:line</c>, the path relative to the solution
///         directory with forward slashes and the lines ordered by path and then by line. The summary line
///         counts the rules checked, passed, failed and skipped.
///     </para>
///     <para>
///         A Migrate rule and a quarantine's containment rule also report how many violations their baseline
///         grandfathers; those pass, so they are never listed as sites. When such a rule fails on violations
///         a baseline could hold and none has been captured yet, the block ends with a hint to run
///         <c>sliderule baseline init</c>, except where a pattern in one of the rule's lists matched nothing,
///         since that command skips such a rule. Where a baseline section no rule names exists, a warning
///         naming that section replaces the hint, and the warning's own hint offers
///         <c>sliderule baseline rename</c>, with <c>init</c> as well on a rule measured in full. A rule whose
///         subject swept generated types says so on a <c>subject:</c> line, and a tripwire that fired prints
///         its warnings followed by the scope's dragons prose. Where the rule itself is what went wrong, a
///         <c>hint:</c> line under the diagnosis says what to change, printed once however many diagnoses of
///         the rule share it. That is the case where its subject or a part of it matched nothing, where a
///         forbidden target matched nothing so the rule can never fire, or where a pattern in one of its lists
///         or a part of its exemplar matched nothing. The rule's own <c>fix:</c> line is left out where the run
///         reached no verdict about the code, because it advises correcting a violation and such a rule has none
///         to correct; a rule that failed on what its other targets found keeps it.
///     </para>
/// </remarks>
internal static class HumanReportRenderer
{
    /// <summary>
    ///     Writes the whole report, every rule block and then the summary line, to
    ///     <paramref name="output" />. Violation sites are rendered relative to
    ///     <paramref name="solutionDirectory" />.
    /// </summary>
    /// <param name="output">The writer the report goes to.</param>
    /// <param name="report">The check report to render.</param>
    /// <param name="solutionDirectory">The directory each violation site's path is printed relative to.</param>
    public static void Render(TextWriter output, CheckReport report, string solutionDirectory)
    {
        RenderReport(output, report, solutionDirectory, includeQuietPasses: true);
    }

    /// <summary>
    ///     The report as <see cref="Render" /> writes it, less the blocks of rules that passed with nothing to
    ///     say: every <c>warn</c>, <c>FAIL</c> and <c>skip</c> block, then the same summary line, which still
    ///     counts the rules it leaves out.
    /// </summary>
    /// <remarks>
    ///     For a reader that already holds the rule board and wants only what a verdict or a warning turned
    ///     on. The pass blocks grow with the spec while the findings do not, so leaving them out is what
    ///     keeps the findings inside a size budget a whole report outgrows.
    /// </remarks>
    internal static void RenderWithoutPasses(TextWriter output, CheckReport report, string solutionDirectory)
    {
        RenderReport(output, report, solutionDirectory, includeQuietPasses: false);
    }

    private static void RenderReport(
        TextWriter output, CheckReport report, string solutionDirectory, bool includeQuietPasses)
    {
        // One relativizer for the whole report: the solution directory is the same string for every site,
        // and a migration report on a legacy codebase renders tens of thousands of them.
        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        foreach (RuleResult result in report.Results)
            if (includeQuietPasses || !PassedQuietly(result))
                RenderRule(output, result, relativizer);

        output.WriteLine();

        string rules = Plurals.Noun(report.RulesChecked, "rule");
        string violations = Plurals.Noun(report.ViolationCount, "violation");
        string warnings = Plurals.Noun(report.WarningCount, "warning");
        output.WriteLine(
            $"Checked {report.RulesChecked} {rules}: {report.RulesPassed} passed, {report.RulesFailed} failed, " +
            $"{report.RulesSkipped} skipped ({report.ViolationCount} {violations}, {report.WarningCount} {warnings}).");
    }

    /// <summary>
    ///     One rule's block as <see cref="Render" /> writes it, returned with LF line endings and no
    ///     trailing newline. This is the message the test adapter fails a test with, so a rule reads the
    ///     same whether it was checked by <c>sliderule check</c> or by a test.
    /// </summary>
    /// <param name="result">The rule result to render.</param>
    /// <param name="solutionDirectory">The directory each violation site's path is printed relative to.</param>
    /// <returns>The rule's block as text.</returns>
    public static string RuleBlock(RuleResult result, string solutionDirectory)
    {
        var writer = new StringWriter { NewLine = "\n" };
        RenderRule(writer, result, new PathFormat.Relativizer(solutionDirectory));
        return writer.ToString().TrimEnd('\n');
    }

    private static void RenderRule(TextWriter output, RuleResult result, PathFormat.Relativizer relativizer)
    {
        string header = result.Rule.Sentence.Length > 0
            ? $"{Marker(result)} {result.Rule.Id} — {result.Rule.Sentence}"
            : $"{Marker(result)} {result.Rule.Id}";
        output.WriteLine(header);

        if (result.Status == RuleStatus.Skipped)
        {
            output.WriteLine($"  skipped: {result.SkipReason}");
            return;
        }

        if (result.Status == RuleStatus.Failed)
        {
            output.WriteLine($"  because: {result.Rule.Because}");
            if (result.Rule.Citation is { } citation) output.WriteLine($"  citation: {citation}");
            // The reason and the page it rests on are facts about the rule and stay whatever happened; the fix
            // is advice for correcting a violation of it, so on a rule that reached no verdict about the code
            // it is advice for a situation that did not arise — sitting directly above the authoring hint
            // that says what actually went wrong.
            if (ReachedAVerdictAboutTheCode(result))
            {
                if (result.Rule.Fix is { } fix) output.WriteLine($"  fix: {fix}");
                // The exemplar is the fix's pointer, so it stands under the same gate and directly after it.
                if (result.Exemplar.Count > 0) output.WriteLine($"  exemplar: {ExemplarText(result, relativizer)}");
            }
        }

        RenderSubjectLine(output, result);

        // Split from the because/fix block above rather than joined to it: the subject line renders for a
        // passing rule too, and it belongs with the rule's own framing rather than after its site list.
        if (result.Status == RuleStatus.Failed)
            foreach (string line in ViolationLines(result, relativizer))
                output.WriteLine($"  {line}");

        if (result.Rule.IsRatcheted) RenderRatchetLines(output, result);

        // Every warning line, and each distinct cure once, under the first warning it cures: two dead
        // patterns of one shape share a sentence, and the empty-subject diagnoses above dedupe theirs the
        // same way.
        var hinted = new HashSet<string>(StringComparer.Ordinal);
        foreach (CheckWarning warning in result.Warnings)
        {
            output.WriteLine($"  warning: {warning.Message}");
            if (warning.Hint is { } hint && hinted.Add(hint)) output.WriteLine($"  hint: {hint}");
        }

        RenderDragonsLines(output, result);
    }

    // The dragons themselves, under a tripwire that actually fired. The warning above says a changed file is
    // in dragon territory and points at `explain` for the prose; that is one round trip too many when the
    // reader is an agent mid-edit, and the prose is already on the rule. Once per rule, never per warning —
    // the dragons are a fact about the scope, not about which file was touched — and absent entirely when
    // nothing fired, so a silent tripwire still renders as one line.
    private static void RenderDragonsLines(TextWriter output, RuleResult result)
    {
        if (result.Rule.Scope is not { Role: ScopeRole.Tripwire } scope || result.Warnings.Count == 0) return;

        if (scope.Dragons is { } dragons) output.WriteLine($"  dragons: {dragons}");
        if (scope.DragonsDoc is { } dragonsDoc) output.WriteLine($"  dragons-doc: {dragonsDoc}");
    }

    // What the rule's subject actually swept, stated only when some of it is generator output — so a rule
    // aimed squarely at code someone wrote says nothing, and the line's presence is itself the finding.
    // It is a fact, never advice: .Authored() is the cure often enough to name in the docs and wrong often
    // enough that a nag here would be noise on every green run.
    private static void RenderSubjectLine(TextWriter output, RuleResult result)
    {
        if (result.SubjectGeneratedTypes == 0) return;

        string types = Plurals.Noun(result.SubjectTypes, "type");
        output.WriteLine($"  subject: {result.SubjectTypes} {types}, {result.SubjectGeneratedTypes} generated");
    }

    // The ratchet's human lines (Migrate and Quarantine containment). Baselined violations pass, so they
    // are never listed as red sites; the grandfathered count is the only place a reader sees them. A grown
    // pair is red and so already listed above, and the grandfathered count deliberately leaves it out:
    // it counts what passed, and a pair that exceeded its allowance did not.
    private static void RenderRatchetLines(TextWriter output, RuleResult result)
    {
        if (result.Grandfathered.Count > 0)
            output.WriteLine($"  grandfathered: {result.Grandfathered.Count} (baselined; run 'sliderule status' for burndown)");

        RenderGrownLine(output, result);

        // Withheld from a rule not measured in full as well: the verb skips that rule, so the hint would name
        // a command that refuses it, beside the warning that says why. Withheld too where the rule carries an
        // orphaned-baseline warning, whose own hint below names the rename, and init where the rule was
        // measured in full.
        bool namesAnOrphan = result.Warnings.Any(warning => warning.Kind == CheckWarningKind.OrphanedBaseline);
        if (result is { AwaitsFirstCapture: true, MeasuredInFull: true } && !namesAnOrphan)
            output.WriteLine(
                "  hint: no baseline captured for this rule; run 'sliderule baseline init' to grandfather existing violations");
    }

    // Each resolved type by its simple name and its first declaration site, the place a reader opens to copy
    // the pattern; a partial type's other parts are one jump away from there. The types arrive ordered by full
    // name, so the line is stable across runs.
    private static string ExemplarText(RuleResult result, PathFormat.Relativizer relativizer)
    {
        IEnumerable<string> pointers = result.Exemplar.Select(type => ExemplarPointer(type, relativizer));
        return string.Join(", ", pointers);
    }

    private static string ExemplarPointer(TypeNode type, PathFormat.Relativizer relativizer)
    {
        if (type.DeclarationSites.Count == 0) return type.Name;

        SourceLocation first = type.DeclarationSites[0];
        string path = relativizer.Relative(first.FilePath);
        return $"{type.Name} ({path}:{first.Line})";
    }

    // Whether the rule reached any verdict about the CODE, as against about itself. EmptySubject and
    // RuleError are the two kinds that are about the rule — the repo's own phrase for them, on
    // ViolationJson.Hint — so a rule failing only on those was never measured against anything. It is
    // deliberately a second predicate beside RuleResult.HoldsBaselinableViolations rather than a shared bool:
    // the two coincide today and ask different questions, and a kind that gained an identity without becoming
    // a finding about the code (or the reverse) would have to move exactly one of them.
    private static bool ReachedAVerdictAboutTheCode(RuleResult result)
    {
        return result.Violations.Any(violation => violation.Kind is not (ViolationKind.EmptySubject or ViolationKind.RuleError));
    }

    // Why a red site sits on a pair the baseline names. Without this line a reader who looks the pair up
    // finds it captured and reads the report as wrong; with it, the allowance and what the run measured
    // against it are both on the page. The two numbers are sums over the grown pairs, so one line covers
    // however many grew — and it never says which site is the new one, because the count does not know:
    // that lossiness is what buys the measure its immunity to line churn.
    private static void RenderGrownLine(TextWriter output, RuleResult result)
    {
        int grown = result.GrownBaselineEntries;
        if (grown == 0) return;

        var baselined = 0;
        var observed = 0;
        foreach (KeyValuePair<Violation, BaselineEntry> pair in result.GrownEntries)
        {
            baselined += pair.Value.SiteCount ?? 0;
            observed += pair.Key.Sites.Count;
        }

        string pairs = Plurals.Noun(grown, "pair");
        output.WriteLine(
            $"  grown: {grown} grandfathered {pairs} exceeded the baseline site count "
            + $"({baselined} baselined, {observed} observed)");
    }

    private static IEnumerable<string> ViolationLines(RuleResult result, PathFormat.Relativizer relativizer)
    {
        var located = new List<(string Path, int Line, string Text)>();
        var unlocated = new List<string>();
        var hints = new List<string>();

        // Where a one-line violation lands: its first carried site as a jump target, or the unlocated
        // block when it carries none. The site always comes off the violation — every shape kind's
        // evidence is minted where the verb decided it points, and the renderer only reads it.
        void Place(SourceLocation? at, string text)
        {
            if (at is not null)
                located.Add((relativizer.Relative(at.FilePath), at.Line, text));
            else
                unlocated.Add(text);
        }

        foreach (Violation violation in result.Violations)
            switch (violation.Kind)
            {
                // The seven edge kinds place identically — one line per reference site — so they share the
                // loop and differ only in the verb EdgeText picks.
                case ViolationKind.Reference or ViolationKind.MemberUse or ViolationKind.Construction
                    or ViolationKind.Injection or ViolationKind.Catch or ViolationKind.Throw or ViolationKind.Expose:
                    string edgeText = ViolationWording.EdgeText(violation);
                    foreach (SourceLocation site in violation.Sites)
                        located.Add((relativizer.Relative(site.FilePath), site.Line, edgeText));
                    break;
                // The three subject kinds place identically — one line at the first site the verb carried,
                // or the unlocated block — so they share the arm and differ only in the text SubjectText picks.
                case ViolationKind.Shape or ViolationKind.MemberShape or ViolationKind.ProjectShape:
                    Place(violation.Sites.FirstOrDefault(), ViolationWording.SubjectText(violation));
                    break;
                case ViolationKind.EmptySubject:
                    unlocated.Add(violation.Detail ?? "the subject selection matched no types");
                    if (violation.Hint is { } hint && !hints.Contains(hint)) hints.Add(hint);
                    break;
                case ViolationKind.RuleError:
                    unlocated.Add($"error: {violation.Detail}");
                    break;
            }

        foreach (string text in unlocated) yield return text;

        // One line per DISTINCT cure, under the diagnoses it cures: a union with three empty parts states
        // all three, then says once what they share, rather than repeating a sentence three times. Ahead of
        // the located lines so a cure never sits below a site list.
        foreach (string hint in hints) yield return $"hint: {hint}";

        // Ordinal, not culture-aware: the site order is part of the rendered output, which is diffed and
        // pinned, so it must not shift with the machine's locale.
        foreach ((string path, int line, string text) in located
                     .OrderBy(l => l.Path, StringComparer.Ordinal)
                     .ThenBy(l => l.Line)
                     .ThenBy(l => l.Text, StringComparer.Ordinal))
            yield return $"{path}:{line} — {text}";
    }

    private static string Marker(RuleResult result)
    {
        return result.Status switch
        {
            RuleStatus.Failed => "FAIL",
            RuleStatus.Skipped => "skip",
            _ => PassedQuietly(result) ? "pass" : "warn"
        };
    }

    // The rules whose block is headed `pass`: held, with no warning to carry. Marker and the warnings-only
    // rendering ask the same question, so a block that heads `pass` is exactly a block that rendering leaves out.
    private static bool PassedQuietly(RuleResult result)
    {
        return result.Status == RuleStatus.Passed && result.Warnings.Count == 0;
    }
}
