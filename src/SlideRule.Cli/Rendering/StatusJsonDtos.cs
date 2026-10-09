using SlideRule.Checking;
using SlideRule.Model;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

// The wire shape of `status --json` — its own document with its own schemaVersion
// (StatusJsonRenderer.SchemaVersion), distinct from `check --json`. Serialized camelCase, indented, nulls
// omitted. The version moves only for a change of shape: an added key and a new value of an enum the schema
// already carries (`caution`) are additive, and a slot with nothing to say is omitted, so a document that
// needs none of them carries none of their keys.

/// <summary>The root <c>status --json</c> document.</summary>
/// <param name="SchemaVersion">The burndown document's schema version, <see cref="StatusJsonRenderer.SchemaVersion" />.</param>
/// <param name="Solution">
///     The solution's file name — the run's subject, and never a path, so the document is
///     machine-independent.
/// </param>
/// <param name="SpecAssembly">The spec DLL's file name — which spec's rules answered.</param>
/// <param name="Rules">One entry per rule in the spec — status carries no narrowing knob.</param>
/// <param name="PackageAudit">
///     The NuGet audit advisories the load raised, one entry per distinct advisory with the projects that
///     reported it, or null (omitted) when there were none — the same entries <c>check --json</c> carries.
/// </param>
/// <param name="WorkspaceDiagnostics">
///     The workspace-load diagnostics other than the NuGet audit advisories, or null (omitted) when there were
///     none — the same array <c>check --json</c> carries, so they reach an <c>arch_status</c> client, which
///     never sees stderr.
/// </param>
/// <param name="ModelIncomplete">
///     <see langword="true" /> when a project failed to load or a project's NuGet packages are not in the
///     model, so the burndown below counts only what the model holds; null (omitted) otherwise. Stamped
///     whether or not <c>--allow-workspace-diagnostics</c> opted out of failing closed — it states the fact
///     about the model, not the exit code.
/// </param>
/// <param name="FailedProjects">
///     Which projects failed to load — solution-relative, forward-slashed <c>.csproj</c> paths — or null
///     (omitted) when none did: half the evidence behind <see cref="ModelIncomplete" />, which
///     <c>workspaceDiagnostics</c> cannot be read for, since MSBuild's words about a fatal failure and about
///     an ordinary restore warning arrive in the same shape.
/// </param>
/// <param name="RestoreFailedProjects">
///     Which projects' NuGet packages are not in the model — solution-relative, forward-slashed
///     <c>.csproj</c> paths — or null (omitted) when none are. The burndown reads low in a quieter way here
///     than it does for <see cref="FailedProjects" />: these projects declare all their types, so the rules
///     over them run and report — a rule whose target is a package the restore never fetched simply finds
///     nothing to count.
/// </param>
/// <param name="UncheckedProjects">
///     Which projects the solution declares that this run never checked — solution-relative,
///     forward-slashed <c>.csproj</c> paths — or null (omitted) when the burndown covers the whole solution.
///     Non-empty only under a <c>.slnf</c> solution filter. An unchecked project declares no types and so
///     contributes no violations, which reads as burndown rather than as absence: every count below is low
///     by whatever these projects hold. Measured as what the solution declares minus what loaded, so a
///     filter that narrows nothing omits the key.
/// </param>
/// <param name="UnsupportedProjects">
///     Which projects the solution declares that this product cannot read — each a solution-relative,
///     forward-slashed project path with the reason — or null (omitted) for an all-C# solution. The burndown
///     reads low here the same way <see cref="UncheckedProjects" /> makes it read low, and permanently: a
///     project the extractor cannot read will never contribute a violation to burn down, so its zero is a
///     property of this product rather than progress the team made.
/// </param>
/// <param name="Orphans">
///     The baseline sections filed under a rule ID the spec does not declare, such as a renamed rule's old
///     section, then the baseline files that could not be read, or null (omitted) when the scan found neither.
/// </param>
/// <param name="Summary">The roll-up: rule counts plus the ratchet burndown totals.</param>
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
///     (<see cref="Path" /> and <see cref="Refused" />).
/// </summary>
/// <param name="RuleId">The rule ID the orphaned section is filed under, or null (omitted) for an unreadable file.</param>
/// <param name="Path">The baseline file, solution-relative and forward-slashed.</param>
/// <param name="Entries">How many entries the orphaned section holds, or null (omitted) for an unreadable file.</param>
/// <param name="Refused">Why the file could not be read, or null (omitted) for an orphaned section.</param>
internal sealed record StatusOrphanJson(string? RuleId, string Path, int? Entries, string? Refused);

/// <summary>
///     One rule's status. <see cref="Ratchet" /> is populated for ratcheted rules (Migrate and Quarantine
///     containment). <see cref="Posture" /> and <see cref="Status" /> are the model's own enums, cased for
///     the wire by <see cref="SlideRuleJson.Options" /> — the same values <c>check --json</c> writes.
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
/// <param name="BaselinePath">The rule's baseline file, solution-relative with forward slashes.</param>
/// <param name="Captured">Whether a baseline section exists for the rule at all.</param>
/// <param name="Remaining">How many violations the baseline currently blesses — the burndown, in pairs.</param>
/// <param name="RemainingSites">
///     How many <em>sites</em> those pairs cover — the burndown at the grain the ratchet measures, and the
///     unit a team actually works off. Unconditional rather than omitted where it equals
///     <see cref="Remaining" />: a burndown figure that vanishes when it happens to agree with its
///     neighbour is one a consumer has to reconstruct, and it is computable whether or not any entry has
///     recorded a count yet.
/// </param>
/// <param name="NewViolations">How many violations are red — new code in the old pattern, growth included.</param>
/// <param name="Stale">
///     How many captured entries no current violation matched: debt since fixed on a rule whose unmatched
///     entries the run tested — a rule <see cref="StricterOperands" /> marks stricter than written included —
///     and entries that may have gone untested on a rule <see cref="MatchedNothing" /> or
///     <see cref="UntestedOperands" /> marks.
/// </param>
/// <param name="Shrunk">
///     How many matched edge entries came in under the count they record, or null (omitted) when none did.
/// </param>
/// <param name="Uncounted">
///     How many matched edge entries record no count at all, or null (omitted) when none. Both measures are
///     omitted at zero, so a rule whose section is fully counted and still holding carries neither key.
/// </param>
/// <param name="Promotable">Whether the Migrate ratchet has burned to zero; omitted for Quarantine.</param>
/// <param name="MatchedNothing">
///     <see langword="true" /> when the rule's own selection matched nothing, so it was reported without
///     being measured against the code and <see cref="Stale" /> counts entries that went untested rather
///     than debt that was paid; null (omitted) otherwise, a rule with a dead target pattern included, since
///     <see cref="UntestedOperands" /> names that one. The cure rides <c>check --json</c>, on the violation
///     for an empty subject and on the warning for an inert target.
/// </param>
/// <param name="UntestedOperands">
///     The operands whose death may hide violations — target patterns that matched no types while the rule's
///     other targets matched some, and <c>among:</c> parts that matched none — each as the check's warning
///     quotes it and in operand order, or null (omitted) when there are none. Present means the rule was not
///     measured in full: <see cref="Stale" /> counts entries that may have gone untested rather than been
///     fixed, <see cref="Promotable" /> is false, and <c>baseline</c> skips the rule in both modes. The cure
///     rides <c>check --json</c>, on each warning.
/// </param>
/// <param name="StricterOperands">
///     The patterns that made the rule stricter than written — allow-list operands and memberships that
///     matched no types — each as the check's warning quotes it and in operand order, or null (omitted) when
///     there are none. Such a pattern only adds violations, so <see cref="Stale" /> still counts fixed debt
///     that <c>baseline accept-reductions</c> retires; <see cref="Promotable" /> is false and
///     <c>baseline init</c> skips the rule, since a capture would record the violations the pattern caused.
///     The cure rides <c>check --json</c>, on each warning.
/// </param>
/// <param name="Cells">
///     Where the remaining debt sits, for a rule whose subject is a family (<c>arch.Each</c>) — one entry
///     per cell that still holds a tolerated pair, in the order the family declares its cells, summing to
///     <see cref="Remaining" /> and <see cref="RemainingSites" />. Null (omitted) for every other rule and
///     for a family rule with nothing left, so a consumer that never asked for it sees the document it saw
///     before. A cell whose pairs are all fixed drops out rather than reading zero: the array says where
///     work remains, and a project family can name every project in a solution.
/// </param>
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

/// <summary>One cell's share of a family rule's remaining debt.</summary>
/// <param name="Name">The cell — the layer's name, or the project's.</param>
/// <param name="Remaining">How many tolerated pairs this cell's law holds.</param>
/// <param name="RemainingSites">How many source sites those pairs cover between them.</param>
internal sealed record RatchetCellJson(string Name, int Remaining, int RemainingSites);

/// <summary>The roll-up: rule counts plus the ratchet burndown totals.</summary>
/// <param name="RulesChecked">How many rules the run evaluated.</param>
/// <param name="RulesPassed">How many of them passed.</param>
/// <param name="RulesFailed">How many of them failed.</param>
/// <param name="RulesSkipped">How many reached no verdict.</param>
/// <param name="GrandfatheredRemaining">The whole solution's burndown, in pairs.</param>
/// <param name="GrandfatheredSites">
///     The same burndown in sites — <see cref="RatchetStatusJson.RemainingSites" /> totalled, and
///     unconditional for its reason.
/// </param>
/// <param name="FixedAwaitingAcceptance">
///     Stale entries across the rules whose unmatched entries this run tested, a rule stricter than written
///     included (<see cref="CheckReport.StaleBaselineEntryCount" />). An entry left unmatched by any other rule
///     is counted by <see cref="Unmeasured" /> instead, so the two partition every unmatched entry and the
///     sum of the per-rule <see cref="RatchetStatusJson.Stale" /> counts.
/// </param>
/// <param name="Shrunk">Shrunk entries across every rule, or null (omitted) when there are none.</param>
/// <param name="Uncounted">Uncounted entries across every rule, or null (omitted) when there are none.</param>
/// <param name="Unmeasured">
///     Entries left unmatched by a rule whose unmatched entries the run could not test (one whose own selection
///     matched nothing, or one with a target pattern or <c>among:</c> part that did), across every rule
///     (<see cref="CheckReport.UnmeasuredBaselineEntryCount" />), or null (omitted) when there are none. Debt
///     of unknown size, not progress.
/// </param>
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
