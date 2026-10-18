using System.Text.Json;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Rendering;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as the <c>--json</c> document, whose <c>schemaVersion</c> is
///     <see cref="SchemaVersion" /> — the only content on stdout in JSON mode, so hooks can parse it.
/// </summary>
/// <remarks>
///     <para>
///         Every site path is written solution-relative with forward slashes, so the document is
///         machine-independent. Serialization lives here so Core stays dependency-free.
///     </para>
///     <para>
///         The document is composed as a string rather than written straight out, so a caller with a
///         response budget can measure the report and, if it overruns, re-compose it one grain coarser from
///         the same result model — one check, one render per rung walked, and never a document cut
///         mid-array.
///     </para>
/// </remarks>
internal static class JsonReportRenderer
{
    /// <summary>The version of the document this writes.</summary>
    internal const int SchemaVersion = 3;

    /// <summary>
    ///     The report document as a string. <paramref name="grain" /> decides how much of each rule is
    ///     rendered and stamps itself on the document; the counts in <c>summary</c> are of what the run
    ///     evaluated and never move with it, so a coarser report still totals the same solution.
    ///     <paramref name="workspaceDiagnostics" /> is the rendered stream the caller composed (with or
    ///     without merge notes) and <paramref name="trust" /> the stamp it read off the load — the project
    ///     lists the document carries, and the grouped NuGet audit advisories that become
    ///     <c>packageAudit</c>. Both are composed once by the caller and handed to every rung, because a
    ///     ladder renders the same load several times over.
    /// </summary>
    public static string Document(
        CheckReport report,
        string solutionDirectory,
        string solutionName,
        string specAssembly,
        string? diffBase,
        IReadOnlyList<string> workspaceDiagnostics,
        WorkspaceTrustStamp trust,
        IReadOnlyList<string> rulesFilter,
        DocumentGrain grain)
    {
        // One relativizer for the whole document: the base directory is the same string for every site,
        // and normalizing plus splitting it is the constant half of the walk.
        var relativizer = new PathFormat.Relativizer(solutionDirectory);

        // The floor rung elides MSBuild's own words, which is the one array here that scales with neither
        // the spec nor even the codebase but with the load's troubles — one entry per project per framework
        // per complaint. Its actionable half is already keyed above, so what goes is the raw text. The NuGet
        // audit advisories go to a count on the same rung: grouped per advisory they no longer scale with the
        // projects, which is what keeps the rungs above the floor within reach (measured: on a bed whose audit
        // feed was unreachable the raw lines were 96% of this document at every grain), but they are still
        // words about the environment rather than about the verdict.
        bool index = grain >= DocumentGrain.Index;
        (IReadOnlyList<PackageAuditEntry>? packageAuditRows, int? packageAuditCount) =
            DocumentGrains.CoverageRung(trust.PackageAudit ?? [], index);
        bool elideDiagnostics = index && workspaceDiagnostics.Count > 0;

        // Every argument is named, which is what holds the six trust slots to the record's declaration
        // order for a reader; the key order is the DTO's to state.
        var document = new CheckJson(
            SchemaVersion: SchemaVersion,
            Solution: solutionName,
            SpecAssembly: specAssembly,
            Grain: DocumentGrains.Wire(grain),
            DiffBase: diffBase,
            RulesFilter: SlideRuleJson.OmitEmpty(rulesFilter),
            ModelIncomplete: trust.ModelIncomplete,
            FailedProjects: trust.FailedProjects,
            UncheckedProjects: trust.UncheckedProjects,
            RestoreFailedProjects: trust.RestoreFailedProjects,
            UnsupportedProjects: trust.UnsupportedProjects,
            MultiTargetedProjects: trust.MultiTargetedProjects,
            Summary: new SummaryJson(
                report.RulesChecked,
                report.RulesPassed,
                report.RulesFailed,
                report.RulesSkipped,
                report.ViolationCount,
                report.WarningCount),
            Rules: report.Results.Select(r => ToRule(r, relativizer, grain)).ToList(),
            PackageAudit: packageAuditRows,
            PackageAuditCount: packageAuditCount,
            // The exception to the rung rule beside it: this slot is present-and-empty on a clean load, so a
            // report that raised nothing still carries the key it always carried.
            WorkspaceDiagnostics: elideDiagnostics ? null : workspaceDiagnostics,
            WorkspaceDiagnosticCount: elideDiagnostics ? workspaceDiagnostics.Count : null);

        return JsonSerializer.Serialize(document, SlideRuleJson.Context.CheckJson);
    }

    // Two rungs act on a rule, and they answer different questions. Skeleton keeps the prose, because that is
    // what makes it a verdict a reader can act on without a second call: prose scales with the rule count,
    // which is authored and small, while violations and their sites scale with the codebase, which is what
    // actually overruns a channel. Index drops it, and is not competing with skeleton for that reader — it
    // competes with a cut document, which is corrupt JSON. What survives is the id and the verdict, which is
    // exactly the menu the next call needs: rules globs match these ids, and arch_explain takes one and
    // returns that rule whole, prose and all.
    private static RuleJson ToRule(RuleResult result, PathFormat.Relativizer relativizer, DocumentGrain grain)
    {
        bool elideViolations = grain >= DocumentGrain.Skeleton;
        bool elideProse = grain >= DocumentGrain.Index;

        return new RuleJson(
            result.Rule.Id,
            result.Rule.Posture,
            result.Status,
            elideProse ? null : result.Rule.Sentence,
            elideProse ? null : result.Rule.Because,
            elideProse ? null : result.Rule.Fix,
            elideProse ? null : result.Rule.Citation,
            elideProse ? null : ToExemplar(result, relativizer),
            result.SkipReason,
            ToBaseline(result),
            // Both or neither: the denominator alone says nothing worth a key, and the numerator alone is
            // unreadable. Non-zero is the whole gate, which is what makes the pair vanish the moment a rule
            // is narrowed to authored types.
            result.SubjectGeneratedTypes > 0 ? result.SubjectTypes : null,
            result.SubjectGeneratedTypes > 0 ? result.SubjectGeneratedTypes : null,
            result.Violations.Count,
            elideViolations
                ? null
                : result.Violations.Select(v => ToViolation(v, result, relativizer, grain)).ToList(),
            // A warning's hint is prose and leaves with the rest of it at index: warnings are the one thing
            // no grain elides, so an un-elided cure would be the only prose left standing on the floor rung.
            result.Warnings.Select(w => new WarningJson(w.Kind, w.Message, elideProse ? null : w.Hint)).ToList());
    }

    // The exemplar is a pointer for the reader who fixes the rule, so it leaves with the prose at index and
    // stays wherever the fix does. Null rather than an empty array when nothing resolved, so the key is absent
    // exactly when there is nothing to point at, whichever of the three reasons that is.
    private static IReadOnlyList<ExemplarJson>? ToExemplar(RuleResult result, PathFormat.Relativizer relativizer)
    {
        if (result.Exemplar.Count == 0) return null;

        return result.Exemplar
            .Select(type => new ExemplarJson(
                type.FullName,
                type.DeclarationSites
                    .Select(site => new SiteJson(relativizer.Relative(site.FilePath), site.Line))
                    .ToList()))
            .ToList();
    }

    // The baseline block is present for any ratcheted rule (Migrate or Quarantine containment); the model's
    // relative path string rides through verbatim. The two measure counts are omitted at zero rather than
    // written as 0, so a section whose entries all record a count they still hold reads exactly as it did
    // before the measure existed.
    private static BaselineJson? ToBaseline(RuleResult result)
    {
        return result.Rule.BaselinePath is { } path
            ? new BaselineJson(
                path,
                result.Grandfathered.Count,
                result.StaleBaselineEntries,
                SlideRuleJson.OmitZero(result.ShrunkBaselineEntries),
                SlideRuleJson.OmitZero(result.UncountedBaselineEntries))
            : null;
    }

    // The rule comes in whole for the one slot that is a fact about this violation's relationship to the
    // baseline rather than about the violation itself: what its entry allowed, when it is red for exceeding it.
    private static ViolationJson ToViolation(
        Violation violation, RuleResult result, PathFormat.Relativizer relativizer, DocumentGrain grain)
    {
        // The first and largest elision: sites are the one array with no ceiling — a legacy migration
        // burndown carries thousands of them under one rule — so this is the rung that actually compresses.
        // Eliding them skips the relativizer walk too, which is why a coarser render is cheaper as well as
        // smaller.
        bool elideSites = grain >= DocumentGrain.Overview;
        int? grandfatheredSiteCount = result.GrownEntries.TryGetValue(violation, out BaselineEntry? grown)
            ? grown.SiteCount
            : null;

        return new ViolationJson(
            violation.Kind,
            violation.Source?.FullName,
            violation.Target?.FullName,
            violation.Member?.SymbolId,
            violation.Subject?.FullName,
            violation.SubjectMember?.SymbolId,
            violation.SubjectProject?.Name,
            violation.Package?.Name,
            violation.Framework,
            violation.Property,
            violation.PropertyValue,
            violation.Detail,
            violation.Hint,
            grandfatheredSiteCount,
            violation.Sites.Count,
            elideSites
                ? null
                : violation.Sites.Select(s => new SiteJson(relativizer.Relative(s.FilePath), s.Line)).ToList());
    }
}
