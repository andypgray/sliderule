using System.Text.Json;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Diff;
using SlideRule.Model;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as the <c>status --json</c> document (its own
///     <c>schemaVersion</c>, <see cref="SchemaVersion" />) — burndown counts per ratcheted rule.
/// </summary>
internal static class StatusJsonRenderer
{
    /// <summary>The version of the document this writes.</summary>
    internal const int SchemaVersion = 2;

    /// <summary>
    ///     Writes the burndown document as the run's stdout line; <paramref name="workspaceDiagnostics" /> is the
    ///     rendered stream the caller composed, and <paramref name="ages" /> dates the remaining entries.
    /// </summary>
    public static void Render(
        TextWriter output,
        CheckReport report,
        BaselineAges ages,
        string solutionName,
        string specAssembly,
        IReadOnlyList<string> workspaceDiagnostics,
        WorkspaceTrustStamp trust)
    {
        var document = new StatusJson(
            SchemaVersion: SchemaVersion,
            Solution: solutionName,
            SpecAssembly: specAssembly,
            Rules: report.Results
                .Select(result => ToRule(result, ages))
                .ToList(),
            PackageAudit: SlideRuleJson.OmitEmpty(trust.PackageAudit),
            WorkspaceDiagnostics: SlideRuleJson.OmitEmpty(workspaceDiagnostics),
            ModelIncomplete: trust.ModelIncomplete,
            MissingProjects: trust.MissingProjects,
            FailedProjects: trust.FailedProjects,
            UncheckedProjects: trust.UncheckedProjects,
            RestoreFailedProjects: trust.RestoreFailedProjects,
            UnsupportedProjects: trust.UnsupportedProjects,
            StaleSpec: trust.StaleSpec,
            Orphans: Orphans(report.OrphanScan),
            Summary: new StatusSummaryJson(
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

        output.Write(SlideRuleJson.Terminated(JsonSerializer.Serialize(document, SlideRuleJson.Context.StatusJson)));
    }

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

    private static StatusRuleJson ToRule(RuleResult result, BaselineAges ages)
    {
        return new StatusRuleJson(
            result.Rule.Id,
            result.Rule.Posture,
            result.Status,
            result.Violations.Count,
            result.Warnings.Count,
            ToRatchet(result, ages));
    }

    // Every value is the model's own fact, the one the human status line reads too, so the two channels cannot
    // disagree about one run.
    private static RatchetStatusJson? ToRatchet(RuleResult result, BaselineAges ages)
    {
        if (result.Rule.BaselinePath is not { } path) return null;

        bool? promotable = result.Rule.Posture == Posture.Migrate ? result.Promotable : null;
        IReadOnlyList<DatedEntry> dated = ages.Date(result.Rule.Id, result.GrandfatheredEntries);
        EntryRecordJson? oldest = dated
            .Select(entry => entry.Recorded)
            .OfType<EntryRecord>()
            .Select(record => new EntryRecordJson(record.RecordedAt, record.RecordedBy))
            .FirstOrDefault();
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
            SlideRuleJson.OmitEmpty(result.StricterOperands),
            SlideRuleJson.OmitEmpty(dated.Select(ToEntry).ToList()),
            oldest);
    }

    private static RatchetEntryJson ToEntry(DatedEntry dated)
    {
        BaselineEntry entry = dated.Entry;
        return new RatchetEntryJson(
            entry.Source, entry.Target, entry.Subject, dated.Recorded?.RecordedAt, dated.Recorded?.RecordedBy,
            dated.Unknown);
    }

    private static IReadOnlyList<RatchetCellJson>? ToCells(RuleResult result)
    {
        return result.RatchetCells
            ?.Cells.Select(cell => new RatchetCellJson(cell.Name, cell.Remaining, cell.RemainingSites))
            .ToList();
    }
}
