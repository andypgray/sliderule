using System.Text;
using System.Text.Json;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Rendering;
using SlideRule.Roslyn.Caching;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as a SARIF 2.1.0 file, for GitHub/ADO code scanning and IDE
///     problem imports.
/// </summary>
/// <remarks>
///     Site-less violations (EmptySubject, RuleError) contribute no results and still gate through the CLI
///     exit code. The log composes its own pointers, stamps and notifications rather than borrowing the human
///     report's; a violation's wording is the exception, with <see cref="ViolationWording" /> as its one owner.
/// </remarks>
internal static class SarifReportRenderer
{
    private const string SchemaUri = "https://json.schemastore.org/sarif-2.1.0.json";
    private const string SarifVersion = "2.1.0";
    private const string DriverName = "SlideRule";
    private const string InformationUri = "https://github.com/andypgray/sliderule";
    private const string SrcRootBaseId = "SRCROOT";
    private const string FingerprintKey = "slideRuleViolationIdentity/v1";
    private const string ErrorLevel = "error";
    private const string NoteLevel = "note";
    private const string WarningLevel = "warning";
    private const string ExemplarSource = "exemplar";

    // UTF-8 without a BOM: SARIF consumers read UTF-8, and a leading BOM would churn the byte-level golden.
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    ///     Writes the SARIF file for <paramref name="report" /> to <paramref name="sarifPath" /> —
    ///     <see cref="Serialize" /> then an atomic, directory-creating write (UTF-8 no BOM, one trailing
    ///     newline). Throws on a bad path or I/O failure rather than swallowing, so the top-level handler maps
    ///     it to exit 2.
    /// </summary>
    public static void Render(
        string sarifPath,
        CheckReport report,
        string solutionDirectory,
        bool executionSuccessful,
        IReadOnlyList<string> workspaceDiagnostics,
        WorkspaceTrustStamp trust)
    {
        string json = Serialize(report, solutionDirectory, executionSuccessful, workspaceDiagnostics, trust);
        AtomicFile.WriteAllBytes(sarifPath, Utf8NoBom.GetBytes(json + "\n"));
    }

    /// <summary>
    ///     Serializes <paramref name="report" /> to the SARIF 2.1.0 JSON string, with no workspace or filesystem
    ///     behind it. <paramref name="solutionDirectory" /> makes every site path solution-relative, and
    ///     <paramref name="executionSuccessful" /> becomes the invocation verdict, false when the
    ///     incomplete-model gate will exit 2.
    /// </summary>
    internal static string Serialize(
        CheckReport report,
        string solutionDirectory,
        bool executionSuccessful,
        IReadOnlyList<string> workspaceDiagnostics,
        WorkspaceTrustStamp trust)
    {
        // One relativizer for the whole log: the solution directory is the same string for every site, and
        // normalizing plus splitting it is the constant half of the walk.
        var relativizer = new PathFormat.Relativizer(solutionDirectory);

        var driver = new SarifDriver(DriverName, ServerVersion.SemVer, InformationUri, BuildRules(report, relativizer));

        var run = new SarifRun(
            new SarifTool(driver),
            BuildInvocations(executionSuccessful, trust, workspaceDiagnostics),
            BuildOriginalUriBaseIds(),
            BuildResults(report, relativizer));
        var log = new SarifLog(SchemaUri, SarifVersion, [run]);
        return JsonSerializer.Serialize(log, SlideRuleJson.Context.SarifLog);
    }

    // Every rule, in model order — passed and skipped rules included (metadata carries the whole spec, not
    // just what failed). A scope tripwire, of either posture, has no sentence, so its shortDescription is
    // omitted.
    private static IReadOnlyList<SarifReportingDescriptor> BuildRules(
        CheckReport report, PathFormat.Relativizer relativizer)
    {
        return report.Results
            .Select(result => new SarifReportingDescriptor(
                result.Rule.Id,
                result.Rule.Sentence.Length > 0 ? new SarifMessage(result.Rule.Sentence) : null,
                new SarifMessage(result.Rule.Because),
                Help(result, relativizer),
                result.Rule.Citation,
                new SarifReportingConfiguration(DefaultLevel(result.Rule)),
                new SarifRuleProperties(result.Rule.Posture)))
            .ToList();
    }

    // The rule's help, in both registers. The citation rides here as well as in helpUri because a scanning
    // service need not read helpUri — GitHub code scanning does not — and help.markdown is what it displays
    // instead, so a citation that appeared only in helpUri would be invisible exactly where a reader is
    // standing over the alert. The markdown twin is written only when there is a link to render: with a fix
    // and no citation the two registers would be the same string, and one of them is the whole point of the
    // pair. The angle brackets are a CommonMark autolink, which is what keeps the sentence's period out of
    // the link target. The exemplar clause sits between the two, after the advice it points at and before the
    // page the reason rests on; it is read off the result rather than the rule, because only the run knows
    // where the type is declared, and a rule whose exemplar resolved to nothing has no clause to print.
    private static SarifMessage? Help(RuleResult result, PathFormat.Relativizer relativizer)
    {
        ArchRule rule = result.Rule;
        var clauses = new List<string>();
        if (rule.Fix is { } fix) clauses.Add(fix);
        if (result.Exemplar.Count > 0) clauses.Add($"Exemplar: {HumanReportRenderer.ExemplarText(result, relativizer)}.");
        string advice = string.Join(" ", clauses);

        if (rule.Citation is not { } citation) return advice.Length > 0 ? new SarifMessage(advice) : null;

        string prefix = advice.Length > 0 ? advice + " " : string.Empty;
        return new SarifMessage($"{prefix}See {citation}.", $"{prefix}See <{citation}>.");
    }

    // The severity a rule reports at when it reports, which for the scope tripwires is not error: a tripwire
    // warns and can never fail, so an error-level descriptor advertises an alert it has
    // no way to raise — and a scanning service that reads defaultConfiguration to set an alert's severity
    // would file the touch as an error. Keyed on the rule's own payload rather than its posture, because a
    // quarantine's other half is the containment law and that one is red — so a posture added later takes
    // its level from the role it declares, with no edit here.
    private static string DefaultLevel(ArchRule rule)
    {
        return rule.IsTripwire ? WarningLevel : ErrorLevel;
    }

    // Exactly one invocation. executionSuccessful is false when the workspace-diagnostics gate will exit 2;
    // the diagnostics themselves ride as warning-level notifications — each NuGet audit advisory as one, ahead
    // of the stream's own, in the order stderr writes them — and the structured facts add one
    // notification each after them, in the order the CLI refusals state them: the model being wrong outranks
    // the model being small, and a failed load outranks a failed restore. Each is written where the stamp
    // carries it: the stamp's empty-to-null policy is the presence test. The notifications are omitted when
    // there is nothing to say, so a clean run's invocation carries none.
    private static IReadOnlyList<SarifInvocation> BuildInvocations(
        bool executionSuccessful, WorkspaceTrustStamp trust, IReadOnlyList<string> workspaceDiagnostics)
    {
        IEnumerable<string> advisories = trust.PackageAudit.Select(entry => entry.Describe());
        List<SarifNotification> notifications = advisories
            .Concat(workspaceDiagnostics)
            .Select(text => new SarifNotification(new SarifMessage(text), WarningLevel))
            .ToList();

        if (trust.FailedProjects is { } failed) notifications.Add(LoadFailureNotification(failed));
        if (trust.RestoreFailedProjects is { } restoreFailed)
            notifications.Add(RestoreFailureNotification(restoreFailed));
        if (trust.UncheckedProjects is { } narrowed) notifications.Add(NarrowingNotification(narrowed));
        if (trust.UnsupportedProjects is { } unsupported) notifications.Add(UnsupportedNotification(unsupported));

        return [new SarifInvocation(executionSuccessful, notifications.Count > 0 ? notifications : null)];
    }

    // Without it an incomplete model reaches SARIF only as MSBuild's replayed prose, in which a fatal
    // evaluation failure and an ordinary restore warning are indistinguishable — so the one channel that
    // could name the cause names neither. Error rather than warning, and independent of executionSuccessful:
    // the opt-out buys a different exit code, not a different truth about the model.
    private static SarifNotification LoadFailureNotification(IReadOnlyList<string> failedProjects)
    {
        string subject = Plurals.Counted(failedProjects.Count, "project");

        return new SarifNotification(
            new SarifMessage(
                $"The model is incomplete: {subject} failed to load, so these results were reached against a "
                + $"codebase missing whole projects: {string.Join(", ", failedProjects)}"),
            ErrorLevel);
    }

    // The quieter half of the same fact, and the one a reader cannot recover by noticing something absent:
    // these projects are all present with all their types, and only the edges their package references would
    // have produced are gone, which is how a failing rule turns green with nothing reported. Worded for both ways
    // that happens, a restore that ran and failed and one that never ran, because the remedy and the
    // consequence are identical and this channel has no room to say which.
    private static SarifNotification RestoreFailureNotification(IReadOnlyList<string> restoreFailedProjects)
    {
        string subject = Plurals.Counted(restoreFailedProjects.Count, "project");

        return new SarifNotification(
            new SarifMessage(
                $"The model is incomplete: NuGet packages did not resolve for {subject}, so these results were "
                + "reached against a codebase whose package references resolved to nothing: "
                + string.Join(", ", restoreFailedProjects)),
            ErrorLevel);
    }

    // A narrowed run's results describe part of the solution, and code scanning has no exit code to read
    // that from — a clean SARIF over a filter would close every alert the unchecked projects would have
    // raised. Warning rather than error: the results are true, they are not the whole solution's.
    private static SarifNotification NarrowingNotification(IReadOnlyList<string> uncheckedProjects)
    {
        string subject = DeclaredProjectsClause.Subject(uncheckedProjects.Count, "checked");

        return new SarifNotification(
            new SarifMessage(
                $"A solution filter narrowed this run: {subject}, so these results cover part of the "
                + $"solution: {string.Join(", ", uncheckedProjects)}"),
            WarningLevel);
    }

    // The other half of "these results cover part of the solution", and the half no filter explains: a
    // project written in a language this product cannot read is never in the model, so no alert can ever be
    // raised in it and a clean scan over a polyglot solution silently reads as clean over all of it. Warning
    // rather than error, beside the narrowing one and for its reason: the results are true, they are not the
    // whole solution's. The reason each entry carries is the shared stamp's, so the prose and the JSON key
    // cannot disagree about what the run could read.
    private static SarifNotification UnsupportedNotification(IReadOnlyList<UnsupportedProjectStamp> unsupportedProjects)
    {
        string subject = DeclaredProjectsClause.Subject(unsupportedProjects.Count, "surveyed");
        IEnumerable<string> entries = unsupportedProjects
            .Select(project => $"{project.Project} ({project.Reason})");

        return new SarifNotification(
            new SarifMessage(
                $"{subject}, so these results cover part of the solution: {string.Join(", ", entries)}"),
            WarningLevel);
    }

    // {"SRCROOT": {}} — the one solution-root URI base every artifact location resolves against, so no
    // absolute path is ever written.
    private static IReadOnlyDictionary<string, SarifArtifactLocationBase> BuildOriginalUriBaseIds()
    {
        return new Dictionary<string, SarifArtifactLocationBase>(StringComparer.Ordinal)
        {
            [SrcRootBaseId] = new()
        };
    }

    // Results in the locked order: rules in model order → per rule, red Violations then Grandfathered (both
    // already ordered by ArchChecker.Order) then Warnings → each violation's Sites in stored order (one
    // result per site).
    // A grown pair is red with the rest, and its state is `updated` rather than `new`: code scanning already
    // has an alert for this pair from the run that baselined it, and `new` would ask for a second one.
    // Unsuppressed, because the whole finding is that the suppression the entry granted no longer covers it.
    // A grandfathered site's attribution is one string minted here and landing twice — as the suppression's
    // justification, and again in the message — because the message is the only one of the two a code-scanning
    // alert shows, that channel reading a result's level and message and ignoring the suppression entirely.
    // Warnings come last per rule, where the human report puts them, and they are the one result kind that is
    // not a violation at all — see WarningResult.
    private static IReadOnlyList<SarifResult> BuildResults(CheckReport report, PathFormat.Relativizer relativizer)
    {
        var results = new List<SarifResult>();
        foreach (RuleResult result in report.Results)
        {
            foreach (Violation violation in result.Violations)
            {
                string state = result.GrownEntries.ContainsKey(violation) ? "updated" : "new";
                results.AddRange(SiteResults(result.Rule.Id, violation, ErrorLevel, state, null, relativizer));
            }

            // Grandfathered is index-aligned with GrandfatheredEntries (RuleResult invariant), so entry i
            // is the stored baseline entry that blessed violation i — its because becomes the justification.
            for (var i = 0; i < result.Grandfathered.Count; i++)
            {
                Violation violation = result.Grandfathered[i];
                BaselineEntry entry = result.GrandfatheredEntries[i];
                string attribution = entry.Because is { } because
                    ? $"grandfathered: {because}"
                    : $"grandfathered in {result.Rule.BaselinePath}";
                results.AddRange(SiteResults(result.Rule.Id, violation, NoteLevel, "unchanged", attribution, relativizer));
            }

            foreach (CheckWarning warning in result.Warnings) results.Add(WarningResult(result.Rule.Id, warning));
        }

        return results;
    }

    // A check warning as a result: warning level, the message verbatim, and the file the warning names as its
    // location — the tripwire names the changed file, an inert target is about the rule's own operand and so
    // gets no location at all. No region with it: the finding is that the file was touched, and a start line
    // would put the alert on whichever line the renderer picked rather than where anything happened. The
    // fingerprint keys on that file so one touch stays one alert across runs. The baseline state is always
    // `new`, because no baseline holds a warning.
    //
    // The message alone, never the warning's Hint: a hint is authoring advice for whoever writes the rule,
    // and this document is read by code scanning, where an alert stands in front of the whole team.
    //
    // Ordinal 0 always, so the rest of the key is what keeps two warnings of one rule apart. A tripwire warns
    // once per changed file and keys on the path, and a whole-target inert warning is at most one per rule and
    // keys on nothing. A dead-operand warning, whichever role its operand held, is one per distinct dead
    // operand and puts that operand's rendered reference in the target slot, where an edge result puts its
    // target. The reference is the sentence renderer's, so a change to how a selection renders re-keys those
    // alerts once; and it may carry a `|`, a Where description being free text, which a consumer that splits
    // the key on `|` has to allow for. An orphaned-baseline warning is one per orphan section and puts the
    // section's rule ID in the same target slot, with the baseline file holding it as the location and the
    // path slot, so two orphans on one rule are two alerts and one orphan stays one alert across runs. A
    // dead-exemplar warning is one per dead part and puts that part's reference in the target slot too, but
    // with the literal `exemplar` in the source slot: an exemplar and a forbidden target may name the same
    // type, and with the source slot empty for both, a dead target operand and a dead exemplar part on one
    // rule would be one alert.
    private static SarifResult WarningResult(string ruleId, CheckWarning warning)
    {
        IReadOnlyList<SarifLocation> locations = warning.File is { } file
            ? [new SarifLocation(new SarifPhysicalLocation(new SarifArtifactLocation(file, SrcRootBaseId), null))]
            : [];
        string source = warning.Kind == CheckWarningKind.DeadExemplar ? ExemplarSource : string.Empty;
        string target = warning.Key ?? string.Empty;
        Dictionary<string, string> fingerprints = Fingerprint(
            source, target, string.Empty, warning.File ?? string.Empty, 0);

        return new SarifResult(
            ruleId, WarningLevel, new SarifMessage(warning.Message), locations, fingerprints, "new", null);
    }

    // The one fingerprint format, so a sixth slot or a v2 bump cannot reach one result kind and miss the
    // other. A warning's empty slots are written as empty arguments rather than as counted pipes in a literal.
    private static Dictionary<string, string> Fingerprint(
        string source, string target, string subject, string path, int ordinal)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FingerprintKey] = $"v1|{source}|{target}|{subject}|{path}|{ordinal}"
        };
    }

    // One result per site. The partial fingerprint keys the alert as (ruleId, v1|source|target|subject|rel|ord)
    // — the identity slots from BaselineIdentity() (empty slots allowed), and a per-file ordinal reset for each
    // violation, so multiple sites of one violation in one file stay distinct while code motion does not churn it.
    //
    // A per-package or per-framework project violation keys an edge from the project to its item, so two
    // packages declared in one file, one site, one path and ordinal 0 apiece, still part on the target slot,
    // and a consumer keyed on the fingerprint never merges them.
    // A non-null attribution is what grandfathered means here, and it mints the suppression and the message's
    // parenthetical together, so the two renderings of one fact cannot be landed separately by a later edit.
    private static IEnumerable<SarifResult> SiteResults(
        string ruleId,
        Violation violation,
        string level,
        string baselineState,
        string? attribution,
        PathFormat.Relativizer relativizer)
    {
        BaselineEntry? identity = violation.BaselineIdentity();
        string source = identity?.Source ?? string.Empty;
        string target = identity?.Target ?? string.Empty;
        string subject = identity?.Subject ?? string.Empty;
        string body = violation.Text();
        var message = new SarifMessage(attribution is null ? body : $"{body} ({attribution})");
        IReadOnlyList<SarifSuppression>? suppressions = attribution is null
            ? null
            : [new SarifSuppression("external", attribution)];
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (SourceLocation site in violation.Sites)
        {
            string relativePath = relativizer.Relative(site.FilePath);
            int ordinal = ordinals.GetValueOrDefault(relativePath);
            ordinals[relativePath] = ordinal + 1;

            Dictionary<string, string> fingerprints = Fingerprint(source, target, subject, relativePath, ordinal);
            var location = new SarifLocation(
                new SarifPhysicalLocation(
                    new SarifArtifactLocation(relativePath, SrcRootBaseId),
                    new SarifRegion(site.Line)));

            yield return new SarifResult(
                ruleId, level, message, [location], fingerprints, baselineState, suppressions);
        }
    }
}
