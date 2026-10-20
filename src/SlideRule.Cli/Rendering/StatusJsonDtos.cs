using SlideRule.Checking;
using SlideRule.Model;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

// The wire shape of `status --json` — its own document with its own schemaVersion
// (StatusJsonRenderer.SchemaVersion), distinct from `check --json`, whose posture and status values it shares.
// Serialized camelCase, indented, nulls omitted. Every path is solution-relative and forward-slashed, and the
// solution and the spec are named by file name alone. The version moves only for a change of shape: an added
// key and a new value of an enum the schema already carries (`caution`) are additive, and a slot with nothing
// to say is omitted, so a document that needs none of them carries none of their keys.

/// <summary>The root <c>status --json</c> document.</summary>
/// <remarks>
///     <para>
///         <paramref name="Rules" /> has one entry per rule in the spec: status carries no narrowing knob.
///         <paramref name="PackageAudit" /> and <paramref name="WorkspaceDiagnostics" /> carry the entries
///         <c>check --json</c> carries, so the load's words reach an <c>arch_status</c> client, which never sees
///         stderr. <paramref name="Orphans" /> lists the baseline sections filed under a rule ID the spec does not
///         declare, such as a renamed rule's old section, then the baseline files that could not be read.
///     </para>
///     <para>
///         The trust stamps, <paramref name="ModelIncomplete" /> through <paramref name="UnsupportedProjects" />,
///         are <see cref="WorkspaceTrustStamp" />'s, and each makes the burndown read low. A failed, unchecked or
///         unsupported project contributes no violations, and a restore-failed project's rules run and report but
///         find nothing to count where their target is a package the restore never fetched. An unsupported
///         project's zero is permanent, a property of this product rather than progress the team made.
///     </para>
/// </remarks>
internal sealed record StatusJson(
    int SchemaVersion,
    string Solution,
    string SpecAssembly,
    IReadOnlyList<StatusRuleJson> Rules,
    IReadOnlyList<PackageAuditEntry>? PackageAudit,
    IReadOnlyList<string>? WorkspaceDiagnostics,
    bool? ModelIncomplete,
    IReadOnlyList<string>? FailedProjects,
    IReadOnlyList<string>? UncheckedProjects,
    IReadOnlyList<string>? RestoreFailedProjects,
    IReadOnlyList<UnsupportedProjectStamp>? UnsupportedProjects,
    IReadOnlyList<StatusOrphanJson>? Orphans,
    StatusSummaryJson Summary);

/// <summary>
///     One finding of the baseline orphan scan: a section no rule in the spec names (<see cref="RuleId" />,
///     <see cref="Path" /> and <see cref="Entries" />), or a baseline file that could not be read
///     (<see cref="Path" />, and <see cref="Refused" /> saying why).
/// </summary>
internal sealed record StatusOrphanJson(string? RuleId, string Path, int? Entries, string? Refused);

/// <summary>
///     One rule's status. <see cref="Ratchet" /> is populated for ratcheted rules (Migrate and Quarantine
///     containment).
/// </summary>
internal sealed record StatusRuleJson(
    string Id,
    Posture Posture,
    RuleStatus Status,
    int Violations,
    int Warnings,
    RatchetStatusJson? Ratchet);

/// <summary>
///     A ratcheted rule's state: the baseline path, capture flag, and burndown counts. <see cref="Promotable" />
///     is populated for Migrate only (omitted for Quarantine containment — its promotion is a human decision).
/// </summary>
/// <remarks>
///     <para>
///         Each slot is read off the rule's <see cref="RuleResult" />, whose members say what it counts.
///         <paramref name="Remaining" /> is the burndown in pairs, and <paramref name="RemainingSites" /> in the
///         sites those pairs cover, written even where the two agree. <paramref name="NewViolations" /> counts the
///         red violations, growth included. <paramref name="Cells" /> is <see cref="RuleResult.RatchetCells" />,
///         summing to <paramref name="Remaining" /> and <paramref name="RemainingSites" />.
///     </para>
///     <para>
///         <paramref name="MatchedNothing" />, <paramref name="UntestedOperands" /> and
///         <paramref name="StricterOperands" /> mark a rule the run did not measure as written, which
///         <see cref="RuleResult.StaleBaselineEntries" /> and <see cref="RuleResult.Promotable" /> say how to
///         read. Each operand is quoted as the check's warning quotes it, in operand order, and the cure rides
///         <c>check --json</c>: on the violation for an empty subject, and on the warning otherwise.
///     </para>
/// </remarks>
internal sealed record RatchetStatusJson(
    string BaselinePath,
    bool Captured,
    int Remaining,
    int RemainingSites,
    int NewViolations,
    int Stale,
    int? Shrunk,
    int? Uncounted,
    bool? Promotable,
    IReadOnlyList<RatchetCellJson>? Cells,
    bool? MatchedNothing,
    IReadOnlyList<string>? UntestedOperands,
    IReadOnlyList<string>? StricterOperands);

/// <summary>One cell's share of a family rule's remaining debt, as <see cref="RatchetCell" /> counts it.</summary>
internal sealed record RatchetCellJson(string Name, int Remaining, int RemainingSites);

/// <summary>
///     The roll-up: rule counts plus the ratchet burndown totals, each read off the <see cref="CheckReport" />.
/// </summary>
/// <remarks>
///     <paramref name="GrandfatheredSites" /> is written even where it equals
///     <paramref name="GrandfatheredRemaining" />. <paramref name="FixedAwaitingAcceptance" /> is
///     <see cref="CheckReport.StaleBaselineEntryCount" />, and <paramref name="Unmeasured" /> is
///     <see cref="CheckReport.UnmeasuredBaselineEntryCount" />, debt of unknown size rather than progress: the two
///     partition every unmatched entry, so together they sum the per-rule <see cref="RatchetStatusJson.Stale" />
///     counts.
/// </remarks>
internal sealed record StatusSummaryJson(
    int RulesChecked,
    int RulesPassed,
    int RulesFailed,
    int RulesSkipped,
    int GrandfatheredRemaining,
    int GrandfatheredSites,
    int FixedAwaitingAcceptance,
    int? Shrunk,
    int? Uncounted,
    int? Unmeasured);
