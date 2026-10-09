using System.Text.Json;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Model;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as the <c>status --json</c> document (its own
///     <c>schemaVersion</c>, <see cref="SchemaVersion" />) — burndown counts per ratcheted rule (Migrate
///     and Quarantine containment) plus, for Migrate, the promotion flag (omitted for quarantine).
/// </summary>
internal static class StatusJsonRenderer
{
    /// <summary>The version of the document this writes.</summary>
    internal const int SchemaVersion = 2;

    /// <summary>
    ///     Writes the burndown document as the run's stdout line.
    ///     <paramref name="workspaceDiagnostics" /> is the rendered stream the caller composed and
    ///     <paramref name="trust" /> the stamp it read off the load — the project lists the document carries,
    ///     and the grouped NuGet audit advisories that become <c>packageAudit</c>.
    /// </summary>
    public static void Render(
        TextWriter output,
        CheckReport report,
        string solutionName,
        string specAssembly,
        IReadOnlyList<string> workspaceDiagnostics,
        WorkspaceTrustStamp trust)
    {
        var document = new StatusJson(
            SchemaVersion,
            solutionName,
            specAssembly,
            report.Results.Select(ToRule).ToList(),
            trust.PackageAudit,
            SlideRuleJson.OmitEmpty(workspaceDiagnostics),
            trust.ModelIncomplete,
            trust.FailedProjects,
            trust.UncheckedProjects,
            trust.RestoreFailedProjects,
            trust.UnsupportedProjects,
            Orphans(report.OrphanScan),
            new StatusSummaryJson(
                report.RulesChecked,
                report.RulesPassed,
                report.RulesFailed,
                report.RulesSkipped,
                report.GrandfatheredCount,
                report.GrandfatheredSiteCount,
                report.StaleBaselineEntryCount,
                SlideRuleJson.OmitZero(report.ShrunkBaselineEntryCount),
                SlideRuleJson.OmitZero(report.UncountedBaselineEntryCount),
                SlideRuleJson.OmitZero(report.UnmeasuredBaselineEntryCount)));

        output.WriteLine(JsonSerializer.Serialize(document, SlideRuleJson.Context.StatusJson));
    }

    // The orphaned sections, then the files that could not be read, as one array; null for an empty scan, so a
    // document with nothing orphaned carries no key and reads byte for byte as it always did.
    private static IReadOnlyList<StatusOrphanJson>? Orphans(BaselineOrphanScan scan)
    {
        if (scan.IsEmpty) return null;

        IEnumerable<StatusOrphanJson> orphans = scan.Orphans
            .Select(orphan => new StatusOrphanJson(orphan.RuleId, orphan.Path, orphan.Entries, Refused: null));
        IEnumerable<StatusOrphanJson> refused = scan.Unreadable
            .Select(file => new StatusOrphanJson(RuleId: null, file.Path, Entries: null, file.Reason));
        return orphans.Concat(refused)
            .ToList();
    }

    private static StatusRuleJson ToRule(RuleResult result)
    {
        return new StatusRuleJson(
            result.Rule.Id,
            result.Rule.Posture,
            result.Status,
            result.Violations.Count,
            result.Warnings.Count,
            ToRatchet(result));
    }

    // The burndown block for any ratcheted rule (Migrate or Quarantine containment). Whether the ratchet has
    // burned to zero is RuleResult.Promotable's answer, one model fact the human line reads too; the wire
    // shape adds only that quarantine omits the key rather than carrying a permanent false, since
    // Quarantine→Migrate is a human decision this document has nothing to say about. The per-cell split
    // comes off RatchetCells, which is also what composes the human row's sub-line, so the two channels
    // cannot disagree about where one run's debt sits. Whether the rule measured everything it names is one
    // model fact too, read from the same three properties the human sub-line chooses its sentence by, so a
    // client reading `stale` learns it from the predicates the human term is chosen by.
    private static RatchetStatusJson? ToRatchet(RuleResult result)
    {
        if (result.Rule.BaselinePath is not { } path) return null;

        bool? promotable = result.Rule.Posture == Posture.Migrate ? result.Promotable : null;
        return new RatchetStatusJson(
            path,
            result.BaselineCaptured,
            result.Grandfathered.Count,
            result.GrandfatheredSiteCount,
            result.Violations.Count,
            result.StaleBaselineEntries,
            SlideRuleJson.OmitZero(result.ShrunkBaselineEntries),
            SlideRuleJson.OmitZero(result.UncountedBaselineEntries),
            promotable,
            ToCells(result),
            SlideRuleJson.OmitFalse(result.SelectionMatchedNothing),
            SlideRuleJson.OmitEmpty(result.UntestedOperands),
            SlideRuleJson.OmitEmpty(result.StricterOperands));
    }

    private static IReadOnlyList<RatchetCellJson>? ToCells(RuleResult result)
    {
        return result.RatchetCells
            ?.Cells.Select(cell => new RatchetCellJson(cell.Name, cell.Remaining, cell.RemainingSites))
            .ToList();
    }
}
