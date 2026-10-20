using SlideRule.Checking;
using SlideRule.Model;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

// The wire shape of `check --json` (its schemaVersion is JsonReportRenderer.SchemaVersion), pinned by a
// golden test. Serialized camelCase, enum values included, indented, nulls omitted. Clustered in one file:
// these records are one cohesive DTO, not product types. Every path is solution-relative and forward-slashed,
// and the solution and the spec are named by file name alone, so the document is machine-independent.
// The version moves only for a change of shape. A slot that is null on every run with nothing to say — a
// kind-specific member or project slot, a ratchet measure at zero, a trust stamp for a workspace that loaded
// whole and unfiltered, the `grain` of a full-grain report — is omitted, so a document that needs none of
// them is byte-identical to one written before the slot existed; and a widened enum (`caution`,
// `cautionedScopeTouched`) is a value a consumer meets, not a new shape.
// Grain thins the report as DocumentGrain's rungs say, and a slot a rung elides is null: "not rendered at
// this grain", never "none found". The prose that index elides is `sentence`, `because`, `fix`, `citation`,
// `exemplar` and each warning's `hint`. `violations`, `sites`, `warnings` and `workspaceDiagnostics` are
// written even when empty. The per-rule `violationCount` and per-violation `siteCount` are unconditional at
// every grain, each declared ahead of the array it summarizes, and at index `packageAuditCount` and
// `workspaceDiagnosticCount` stand in for their arrays where those had entries, never as a bare 0.
// Declaration order IS the wire order (System.Text.Json writes it verbatim), and CheckJson's is a deliberate
// ranking: the request echo, then the trust stamps, then `summary`, then `rules`, with `packageAudit` and then
// `workspaceDiagnostics` last — a stamp never arrives after the counts it invalidates, and a downstream cut
// lands in the bulk rather than on the roll-up.

/// <summary>The root JSON document — the only thing written to stdout in <c>--json</c> mode.</summary>
/// <remarks>
///     <para>
///         <paramref name="Grain" /> is <see cref="DocumentGrains.Wire" />. <paramref name="DiffBase" /> is the
///         git ref the Quarantine tripwire compared against. <paramref name="RulesFilter" /> is the rule-ID globs
///         the run was narrowed to; present, <c>rules</c> and <c>summary</c> cover only the rules it selected, so
///         a clean narrowed document is not a clean solution.
///     </para>
///     <para>
///         The trust stamps, <paramref name="ModelIncomplete" /> through <paramref name="MultiTargetedProjects" />,
///         are <see cref="WorkspaceTrustStamp" />'s. <paramref name="FailedProjects" /> and
///         <paramref name="RestoreFailedProjects" /> are the evidence behind <paramref name="ModelIncomplete" />,
///         which means every verdict was reached against a partial model. The unchecked, unsupported and
///         multi-targeted lists scope the verdict rather than invalidate it: every rule ran and answered, over
///         less of the solution, or, for a type a multi-targeted project's frameworks share, over the members and
///         hierarchy of the first framework that declares it, with its references from every framework.
///     </para>
///     <para>
///         <paramref name="PackageAudit" /> is the NuGet audit advisories, one entry per distinct advisory, which
///         never mark the model incomplete. <paramref name="WorkspaceDiagnostics" /> is MSBuild's own words about
///         the rest of the load, one entry per project per framework per complaint.
///     </para>
/// </remarks>
internal sealed record CheckJson(
    int SchemaVersion,
    string Solution,
    string SpecAssembly,
    string? Grain,
    string? DiffBase,
    IReadOnlyList<string>? RulesFilter,
    bool? ModelIncomplete,
    IReadOnlyList<string>? FailedProjects,
    IReadOnlyList<string>? UncheckedProjects,
    IReadOnlyList<string>? RestoreFailedProjects,
    IReadOnlyList<UnsupportedProjectStamp>? UnsupportedProjects,
    IReadOnlyList<MultiTargetedProject>? MultiTargetedProjects,
    SummaryJson Summary,
    IReadOnlyList<RuleJson> Rules,
    IReadOnlyList<PackageAuditEntry>? PackageAudit,
    int? PackageAuditCount,
    IReadOnlyList<string>? WorkspaceDiagnostics,
    int? WorkspaceDiagnosticCount);

/// <summary>
///     One rule's result. <paramref name="Id" /> is the post-desugar rule ID, the string <c>rules</c> globs match
///     and <c>arch_explain</c> takes, and <paramref name="Baseline" /> is present for a ratcheted rule (Migrate
///     and Quarantine containment).
/// </summary>
/// <remarks>
///     <paramref name="Exemplar" /> is <see cref="RuleResult.Exemplar" />, the code a fix is pointed at.
///     <paramref name="SubjectGeneratedTypes" /> is how many of the subject's types a generator emitted, out of
///     <paramref name="SubjectTypes" />; the pair is present only when the generated count is non-zero, so
///     narrowing the rule with <c>.Authored()</c> removes both keys.
/// </remarks>
internal sealed record RuleJson(
    string Id,
    Posture Posture,
    RuleStatus Status,
    string? Sentence,
    string? Because,
    string? Fix,
    string? Citation,
    IReadOnlyList<ExemplarJson>? Exemplar,
    string? SkipReason,
    BaselineJson? Baseline,
    int? SubjectTypes,
    int? SubjectGeneratedTypes,
    int ViolationCount,
    IReadOnlyList<ViolationJson>? Violations,
    IReadOnlyList<WarningJson> Warnings);

/// <summary>A ratcheted rule's state: its baseline path and how the run's violations matched its entries.</summary>
/// <remarks>
///     <paramref name="Grandfathered" /> counts the violations the baseline blessed, so a pair grown past the
///     site count its entry records is not here: it is red, in <c>violations</c>, carrying
///     <see cref="ViolationJson.GrandfatheredSiteCount" />. <paramref name="Stale" /> counts the captured entries
///     no current violation matched. Of the matched edge entries, <paramref name="Shrunk" /> counts those under
///     the site count they record, which pass and await <c>baseline accept-reductions</c> to lower it, and
///     <paramref name="Uncounted" /> those that record none, which grandfather their pair at any size.
/// </remarks>
internal sealed record BaselineJson(string Path, int Grandfathered, int Stale, int? Shrunk, int? Uncounted);

/// <summary>
///     One violation. <paramref name="Kind" /> decides which slots are populated: each is the
///     <see cref="Violation" /> member of the same name and null wherever that member is, with types written as
///     full names, members as raw symbol IDs, and projects and packages by name.
/// </summary>
/// <remarks>
///     <paramref name="TargetMember" /> is <see cref="Violation.Member" />, the banned member of a
///     <c>memberUse</c> violation, whose <paramref name="Target" /> is null. <paramref name="SubjectProject" /> is
///     the project's name rather than its <c>project:</c> identity, which is a baseline key.
///     <paramref name="Framework" /> does not say whether the project targets the framework or lacks it; the
///     rule's sentence tells the two apart. <paramref name="GrandfatheredSiteCount" /> is the site count the
///     violation's baseline entry records, present only on a grandfathered pair that is red for having grown past
///     it, and declared ahead of <paramref name="SiteCount" /> so the pair reads as allowance then measurement.
/// </remarks>
internal sealed record ViolationJson(
    ViolationKind Kind,
    string? Source,
    string? Target,
    string? TargetMember,
    string? Subject,
    string? SubjectMember,
    string? SubjectProject,
    string? Package,
    string? Framework,
    string? Property,
    string? PropertyValue,
    string? Detail,
    string? Hint,
    int? GrandfatheredSiteCount,
    int SiteCount,
    IReadOnlyList<SiteJson>? Sites);

/// <summary>A single reference or declaration site.</summary>
internal sealed record SiteJson(string File, int Line);

/// <summary>One type a rule's exemplar resolved to, by full name, with every site that declares it.</summary>
/// <remarks>
///     A partial type has one site per declaring part. The sites stay at overview and skeleton grain, where a
///     violation's go: an exemplar names a handful of types, so its sites never scale with the codebase.
/// </remarks>
internal sealed record ExemplarJson(string Type, IReadOnlyList<SiteJson> Sites);

/// <summary>
///     A non-fatal warning: <see cref="CheckWarning" />'s kind, message and <see cref="CheckWarning.Hint" />, the
///     hint repeated on every warning that carries one where the human report prints it once.
/// </summary>
internal sealed record WarningJson(CheckWarningKind Kind, string Message, string? Hint);

/// <summary>The roll-up counts.</summary>
internal sealed record SummaryJson(
    int RulesChecked,
    int RulesPassed,
    int RulesFailed,
    int RulesSkipped,
    int Violations,
    int Warnings);
