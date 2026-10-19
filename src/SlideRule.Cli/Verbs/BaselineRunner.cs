using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.SpecLoading;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Prose;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Baselines;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;
using SlideRule.Validation;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>baseline</c> pipeline behind its four subcommands: grandfather, shrink, deliberately grow, or rename
///     the ratcheted baselines — both Migrate rules and Quarantine containment (GRAMMAR §7). The <c>add</c>
///     and <c>rename</c> companions are validated first (before the workspace cost).
/// </summary>
/// <remarks>
///     <para>
///         <c>init</c> captures each uncaptured ratcheted rule's current violations, and
///         <c>accept-reductions</c> only ever tightens: it drops entries whose violation no longer occurs
///         and lowers or first records site counts, refusing new entries and rises alike. <c>add</c> is the
///         escape valve and the one route by which an entry or a site count grows: exactly one currently
///         observed violation of a captured rule, with mandatory attribution. <c>rename</c> moves a renamed
///         rule's section from its old ID to its new one, every entry kept and re-sealed; it reads the spec
///         and the baseline files and never the code, so it cannot grandfather anything new, and none of the
///         gates below applies to it.
///     </para>
///     <para>
///         <b>What it refuses.</b> Tamper refuses loudly with the recovery hint, the same as <c>check</c>,
///         and a workspace-load failure refuses the whole command on <c>check</c>'s terms: exit 2, nothing
///         written, opt-out <see cref="BaselineRequest.AllowWorkspaceDiagnostics" />. <c>init</c> and
///         <c>accept-reductions</c> read absence as evidence, so a solution filter that left declared
///         projects unchecked refuses them too, a rule whose unmatched entries the run could not test (its own
///         selection matched nothing, or one of its target patterns or <c>among:</c> parts did) is skipped, and
///         they extract cache-free, because a stale cache hit is a smaller-than-real model that raises nothing
///         either gate could refuse on. On any of these they would record "zero debt" or delete real entries.
///         <c>init</c> also skips a rule stricter than written, whose capture would record as debt the
///         violations its dead pattern caused.
///         Short of that the command reports rather than gates — a red rule is the state to capture, not a
///         failure, so it exits 0 on success.
///     </para>
///     <para>Output/error writers are injected so the e2e tests can capture them.</para>
/// </remarks>
internal sealed class BaselineRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null)
    : CacheWiredRunner(source, environment)
{
    public async Task<int> RunAsync(BaselineRequest request, CancellationToken ct)
    {
        // rename measures nothing, so none of the gates below has anything to protect in it: it branches out
        // before the workspace cost they sit in front of.
        if (request.Mode == BaselineMode.Rename) return await RenameAsync(request, ct);

        // The add companions FIRST — before discovering a solution or loading a workspace. init and
        // accept-reductions have none: the command tree gives them no options to get wrong.
        if (request.Mode == BaselineMode.Add) ValidateAddCompanions(request);

        // init and accept-reductions read absence as evidence, so they extract cache-free: a stale hit is
        // a smaller-than-real model, and unlike a failed load or a solution filter it raises no diagnostic the
        // two gates below could refuse on. add records one violation the run did see, so it may front the
        // cache and the operator's --no-cache is the whole policy there.
        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, request.Solution, request.Spec, request.WorkingDirectory,
            request.ReadsAbsenceAsEvidence || request.NoCache, ct);

        // Composed for the render, so the MSBuild-selection note goes out with the load failures.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        WorkspaceDiagnosticsRenderer.Render(error, diagnostics);

        // Fail closed before extraction, and so before any mode writes a byte: the projects that failed to
        // load or to restore are the whole gate input here, exactly as they are for check, and no diagnostic
        // text reaches it (the merge notes are not even minted until extraction).
        if (IncompleteModelNotices.Refused(
                error, diagnostics, request.AllowWorkspaceDiagnostics, IncompleteModelGate.BaselineMessage))
            return ExitCodes.Error;

        // Then the narrowing refusal, on the gate's own terms and in the same position — before extraction,
        // so nothing is written. Both modes below read absence as evidence: init captures "zero debt" for
        // rules whose subjects the filter left out, and accept-reductions deletes real entries as
        // violations that stopped occurring. add rides through: it records what the run did see.
        if (request.ReadsAbsenceAsEvidence && diagnostics.IsNarrowed)
        {
            NarrowingNotices.Refusal(error, source, NarrowedUniverseNotice.BaselineRefusal);
            return ExitCodes.Error;
        }

        // init names every section no rule reads before it captures: a rule renamed after capture looks exactly
        // like a new one here, and the rename keeps entries a fresh capture would record again as today's debt.
        // A nudge, not a refusal, and orphans only: init writes only declared files and refuses a bad one itself.
        if (request.Mode == BaselineMode.Init)
        {
            BaselineOrphanScan scan = BaselineOrphanScanner.Scan(source.Model, source.SolutionDirectory);
            foreach (BaselineOrphan orphan in scan.Orphans)
                await output.WriteLineAsync(BaselineOrphanWording.Line(orphan));
        }

        CodebaseModel codebase = await source.ExtractAsync(source.Resolution.ExcludeProjectNames, ct);
        RecordCacheOutcome(source);

        // Evaluate against an empty baseline so every current violation surfaces as the state to capture.
        // No narrowing goes down: this report is a capture survey rather than a verdict, the two modes that
        // read absence as evidence have already refused above, and add branches out before the survey.
        CheckReport report = ArchChecker.Check(
            source.Model.Rules, codebase, BaselineIndex.Empty, null, null, null, new SolutionRoot(source.SolutionDirectory));

        // Branch to the single-rule add path before the ratchet survey below, so a bad --rule refuses instead of exiting 0.
        if (request.Mode == BaselineMode.Add)
            return AddEntry(request, report, source.SolutionDirectory);

        List<RuleResult> ratchetResults = report.Results.Where(r => r.Rule.IsRatcheted).ToList();
        if (ratchetResults.Count == 0)
        {
            foreach (string line in RatchetSurveyNotice.Lines(report, anyRatchetedRule: false))
                await output.WriteLineAsync(line);
            return ExitCodes.Clean;
        }

        // The directory is read out before the apply below, because the grouping in there is lazy: a lambda
        // closing over `source` would outlive the using block if the sequence were ever enumerated later.
        string solutionDirectory = source.SolutionDirectory;
        ApplyFiles(request, ratchetResults, solutionDirectory);

        // The survey's last word: name the failing rules no baseline can capture, after the per-file lines.
        foreach (string line in RatchetSurveyNotice.Lines(report, anyRatchetedRule: true))
            await output.WriteLineAsync(line);

        return ExitCodes.Clean;
    }

    // The add companions are coherent — all before the workspace cost. Which options exist at all is the
    // command tree's to say, so nothing here asks whether a mode was chosen or a companion strayed.
    private static void ValidateAddCompanions(BaselineRequest request)
    {
        BaselineAddition addition = request.Addition ?? throw MisassembledAddRequest();
        if (addition.Rule is null)
            throw new UserErrorException("baseline add requires --rule <id>.");

        bool blankOrMultiline =
            string.IsNullOrWhiteSpace(addition.Because) || SingleLineProse.IsMultiLine(addition.Because);
        if (blankOrMultiline)
            throw new UserErrorException("baseline add requires a non-blank, single-line --because.");

        bool edgeForm = addition.Source is not null && addition.Target is not null && addition.Subject is null;
        bool subjectForm = addition.Subject is not null && addition.Source is null && addition.Target is null;
        if (!edgeForm && !subjectForm)
            throw new UserErrorException(
                "baseline add requires exactly one entry form: --source with --target (an edge), or --subject (a shape).");
    }

    // Only the add subcommand builds an addition, and RunAsync validates it before the workspace cost, so an add
    // request without one, or with one that could not have passed that validation, was assembled wrong by code
    // rather than typed wrong by an operator.
    private static InvalidOperationException MisassembledAddRequest()
    {
        return new InvalidOperationException(
            "A baseline add request carries no addition, or one that never passed validation.");
    }

    private static UserErrorException RuleNotInSpec(string ruleId)
    {
        return new UserErrorException($"rule '{ruleId}' is not in the spec.");
    }

    private static UserErrorException RuleNotRatcheted(string ruleId)
    {
        return new UserErrorException(
            $"rule '{ruleId}' is not ratcheted — only Migrate and Quarantine containment rules carry baselines.");
    }

    // add: append one attributed entry to a captured rule's section (or update attribution on a present
    // entry). Internal so the fast-tier runner tests drive it over an in-memory report (no workspace).
    internal int AddEntry(BaselineRequest request, CheckReport report, string solutionDirectory)
    {
        if (request.Addition is not { Rule: { } ruleId, Because: { } because } addition)
            throw MisassembledAddRequest();

        RuleResult? result = report.Results.FirstOrDefault(r => string.Equals(r.Rule.Id, ruleId, StringComparison.Ordinal));
        if (result is null) throw RuleNotInSpec(ruleId);
        // The path rather than IsRatcheted, though they answer the same question: this site needs the file
        // too, and binding it here is what lets the resolve below read a value instead of re-reading a
        // nullable property.
        if (result.Rule.BaselinePath is not { } baselinePath) throw RuleNotRatcheted(ruleId);

        if (CurrentEntries(result) is not { } current)
            throw new UserErrorException(
                $"cannot add to rule '{ruleId}' — the rule has an empty subject or an evaluation error.");

        string path = BaselineStore.ResolvePath(baselinePath, solutionDirectory);
        BaselineDocument? existing = BaselineStore.TryReadDocument(path);
        if (existing is null || !existing.Sections.TryGetValue(ruleId, out IReadOnlyList<BaselineEntry>? existingEntries))
            throw new UserErrorException($"no baseline section for '{ruleId}' — run 'sliderule baseline init' first.");

        Violation violation = addition switch
        {
            { Subject: { } subject } => BaselineAddMatcher.ResolveSubject(ruleId, result.Violations, subject),
            { Source: { } edgeSource, Target: { } edgeTarget } =>
                BaselineAddMatcher.ResolveEdge(ruleId, result.Violations, edgeSource, edgeTarget),
            _ => throw MisassembledAddRequest()
        };

        BaselineEntry identity = violation.BaselineIdentity()!;
        // The measure comes off the folded survey rather than off the resolved violation: the matcher hands
        // back the first candidate of an identity, and where two violations share one the allowance this
        // valve records has to cover the larger. add is the only route by which a count may rise, so
        // recording the whole observed figure here is what makes growth attributable rather than silent.
        (BaselineEntry Identity, int Sites) folded = current.First(entry => entry.Identity.Equals(identity));
        BaselineEntry attributed = Counted(folded.Identity, folded.Sites)
            .WithBecause(because);

        Dictionary<string, IReadOnlyList<BaselineEntry>> sections = MutableSections(existing);

        if (existingEntries.FirstOrDefault(entry => entry.Equals(identity)) is { } stored)
        {
            sections[ruleId] = existingEntries.Select(e => e.Equals(identity) ? attributed : e).ToList();
            output.WriteLine($"{ruleId}: entry already baselined — {ReRecorded(stored, folded.Sites)}");
        }
        else
        {
            sections[ruleId] = existingEntries.Append(attributed).ToList();
            // The shared full-name form covers every kind the matcher resolves — subject, reference edge,
            // and member use (whose Target slot is null; the member display renders instead).
            output.WriteLine(
                $"{ruleId}: added 1 grandfathered entry — {BaselineAddMatcher.FullNameForm(violation)} (because: {because}).");
        }

        WriteOutcome outcome = BaselineStore.Write(path, new BaselineDocument(sections));
        output.WriteLine(WriteReport.Line(outcome, solutionDirectory, path));
        return ExitCodes.Clean;
    }

    // rename: the model the way explain loads it, then the file work. A malformed --from is refused before any
    // solution work, as add's companions are. A built-DLL --spec never opens a workspace; a convention or csproj
    // spec opens one for resolution only, never extracts, and never gates on a failed load, because a project
    // that failed to load cannot change which rules the spec declares and nothing this mode writes is measured.
    private async Task<int> RenameAsync(BaselineRequest request, CancellationToken ct)
    {
        BaselineRename rename = request.Rename ?? throw MisassembledRenameRequest();
        RefuseMalformedFrom(rename.From);

        SpecResolution? withoutSolution = SpecResolver.TryResolveWithoutSolution(request.Spec);
        if (withoutSolution is not null)
        {
            string solutionPath = ModelPipeline.DiscoverSolution(request.Solution, request.WorkingDirectory, Environment);
            string anchor = SolutionProjectFileParser.AnchorDirectory(solutionPath);
            ArchitectureModel model = SolutionSource.LoadSpecModel(withoutSolution.DllPath);
            return Rename(request, model, anchor);
        }

        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, request.Solution, request.Spec, request.WorkingDirectory, false, ct);
        WorkspaceDiagnosticsRenderer.Render(error, source.Diagnostics);
        RecordCacheOutcome(source);

        return Rename(request, source.Model, source.SolutionDirectory);
    }

    // The old ID can become a conventional path that Rename may delete. BaselineConventions refuses an ID outside
    // the grammar (a `..` segment above all) as a programming error; these guards make it an operator's refusal,
    // both before solution work and again at the file work itself, which the fast tier reaches directly.
    private static void RefuseMalformedFrom(string from)
    {
        if (!RuleIdSyntax.IsValid(from))
            throw new UserErrorException(
                $"'{from}' is not a well-formed rule ID; expected area/rule-name matching {RuleIdSyntax.Pattern}.");
    }

    // Only the rename subcommand builds its companions, and the command tree requires both, so a rename request
    // without them was assembled wrong by code rather than typed wrong by an operator.
    private static InvalidOperationException MisassembledRenameRequest()
    {
        return new InvalidOperationException("A baseline rename request carries no --from and --to.");
    }

    // rename's file work: every refusal before any write, then the destination written first, so an interrupted
    // run leaves the entries under both IDs rather than under neither. The entries list moves verbatim —
    // identity, site count and attribution — and the write re-seals each under its new ID. Internal so the
    // fast-tier runner tests drive it over a hand-built model, as they drive add.
    internal int Rename(BaselineRequest request, ArchitectureModel model, string solutionDirectory)
    {
        if (request.Rename is not { From: var from, To: var to }) throw MisassembledRenameRequest();
        RefuseMalformedFrom(from);

        ArchRule? toRule = model.Rules.FirstOrDefault(rule => string.Equals(rule.Id, to, StringComparison.Ordinal));
        if (toRule is null) throw RuleNotInSpec(to);
        if (toRule.BaselinePath is not { } baselinePath) throw RuleNotRatcheted(to);
        if (model.Rules.Any(rule => string.Equals(rule.Id, from, StringComparison.Ordinal)))
            throw new UserErrorException(
                $"rule '{from}' is still in the spec, so its baseline section is not orphaned; rename the rule in the spec first.");

        string destinationPath = BaselineStore.ResolvePath(baselinePath, solutionDirectory);
        BaselineDocument? destination = BaselineStore.TryReadDocument(destinationPath);
        if (destination is not null && destination.Sections.ContainsKey(to))
        {
            string destinationShown = PathFormat.Relative(solutionDirectory, destinationPath);
            throw new UserErrorException(
                $"rule '{to}' already has a baseline section in {destinationShown}; nothing was renamed.");
        }

        // The source is wherever the orphan scan finds the old section, the same files status and check name, and
        // a section filed in several is refused rather than picked from. With no match it is the destination file,
        // else the old ID's conventional file, so a file the reader refuses, which the scan lists as unreadable
        // rather than as an orphan, is still read here and refused.
        string sourcePath = OrphanSource(model, solutionDirectory, from, destinationPath, destination)
                            ?? FallbackSource(solutionDirectory, from, destinationPath, destination);
        bool sameFile = string.Equals(sourcePath, destinationPath, PathComparison.Comparison);
        BaselineDocument? sourceDocument = sameFile ? destination : BaselineStore.TryReadDocument(sourcePath);

        if (sourceDocument is null || !sourceDocument.Sections.TryGetValue(from, out IReadOnlyList<BaselineEntry>? entries))
            throw new UserErrorException(NothingToRename(from, solutionDirectory, destinationPath, sourcePath, sameFile));

        Dictionary<string, IReadOnlyList<BaselineEntry>> destinationSections = MutableSections(destination);
        destinationSections[to] = entries;
        if (sameFile) destinationSections.Remove(from);

        WriteOutcome written = BaselineStore.Write(destinationPath, new BaselineDocument(destinationSections));
        output.WriteLine($"{to}: renamed from {from}, moving {entries.Count} grandfathered {Plurals.Noun(entries.Count, "entry")}.");
        output.WriteLine(WriteReport.Line(written, solutionDirectory, destinationPath));
        if (sameFile) return ExitCodes.Clean;

        Dictionary<string, IReadOnlyList<BaselineEntry>> sourceSections = MutableSections(sourceDocument);
        sourceSections.Remove(from);
        if (sourceSections.Count == 0)
        {
            // An emptied file would be a section-less baseline naming nothing; its directories are left, since
            // git tracks no empty one.
            BaselineStore.Delete(sourcePath);
            output.WriteLine($"deleted {PathFormat.Relative(solutionDirectory, sourcePath)}");
        }
        else
        {
            WriteOutcome kept = BaselineStore.Write(sourcePath, new BaselineDocument(sourceSections));
            output.WriteLine(WriteReport.Line(kept, solutionDirectory, sourcePath));
        }

        return ExitCodes.Clean;
    }

    // The one file the orphan scan lists the old section in, or null when it lists none. The destination was read
    // already and is handed over rather than read twice.
    private static string? OrphanSource(
        ArchitectureModel model, string solutionDirectory, string from, string destinationPath,
        BaselineDocument? destination)
    {
        var alreadyRead = new Dictionary<string, BaselineDocument>(PathComparison.Comparer);
        if (destination is not null) alreadyRead[Path.GetFullPath(destinationPath)] = destination;

        BaselineOrphanScan scan = BaselineOrphanScanner.Scan(model, solutionDirectory, alreadyRead);
        List<string> files = scan.Orphans
            .Where(orphan => string.Equals(orphan.RuleId, from, StringComparison.Ordinal))
            .Select(orphan => orphan.Path)
            .ToList();

        return files.Count switch
        {
            0 => null,
            1 => BaselineStore.ResolvePath(files[0], solutionDirectory),
            _ => throw new UserErrorException(SeveralSources(from, files))
        };
    }

    // The destination file itself when the old section sits there (a shared file renamed in place), and otherwise
    // the old ID's conventional file: the one place a capture under that ID wrote.
    private static string FallbackSource(
        string solutionDirectory, string from, string destinationPath, BaselineDocument? destination)
    {
        bool inPlace = destination is not null && destination.Sections.ContainsKey(from);
        return inPlace
            ? destinationPath
            : BaselineStore.ResolvePath(BaselineConventions.DefaultPath(from), solutionDirectory);
    }

    // Every file holding the old section, in the scan's order. Picking one would leave the others orphaned with no
    // rename left that could move them, so the operator chooses by deleting.
    private static string SeveralSources(string from, IReadOnlyList<string> files)
    {
        string where = ProseFormat.JoinReferencesAnd(files);
        return $"baseline sections for '{from}' are filed in {where}; nothing was renamed. Delete every copy but the one to keep, then run the rename again.";
    }

    // Where the refusal looked, named once when the old ID's conventional file is the destination itself.
    private static string NothingToRename(
        string from, string solutionDirectory, string destinationPath, string sourcePath, bool sameFile)
    {
        string destinationShown = PathFormat.Relative(solutionDirectory, destinationPath);
        string where = sameFile
            ? destinationShown
            : $"{destinationShown} or {PathFormat.Relative(solutionDirectory, sourcePath)}";
        return $"nothing to rename: no baseline section for '{from}' in {where}; if the rule is new, run 'sliderule baseline init'.";
    }

    // The tail of the already-baselined echo. add is the ratchet's only growth valve, so when it moves a
    // site count it says so and names both ends of the move — an operator who meant only to restate the
    // attribution should be able to read that the allowance went up. An entry with no measure to move keeps
    // the plain sentence: a subject entry carries none, and neither does an edge with no sited evidence.
    private static string ReRecorded(BaselineEntry stored, int observedSites)
    {
        RatchetState state = BaselineRatchet.Classify(stored, observedSites);
        if (state is RatchetState.NotMeasured) return "attribution updated.";

        string from = state is RatchetState.Uncounted ? "uncounted" : $"{stored.SiteCount!.Value}";
        return $"attribution updated and site count re-recorded ({from} → {observedSites}).";
    }

    // A read document's sections as a map this run may write into: every section rides through untouched,
    // including the ones for rules this run is not visiting (a co-resident rule, or one that was removed).
    // Null-tolerant because init and accept-reductions reach a file that may not exist yet, and so does
    // rename's destination, while add has already refused an absent one.
    private static Dictionary<string, IReadOnlyList<BaselineEntry>> MutableSections(BaselineDocument? existing)
    {
        var sections = new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal);
        if (existing is not null)
            foreach (KeyValuePair<string, IReadOnlyList<BaselineEntry>> section in existing.Sections)
                sections[section.Key] = section.Value;

        return sections;
    }

    // init and accept-reductions, one baseline file at a time: ordinal grouping in first-appearance order,
    // so two rules sharing a file are read, spliced and written once between them. Internal so the fast-tier
    // runner tests can drive both modes over an in-memory report the way they drive add — no workspace.
    internal void ApplyFiles(BaselineRequest request, IEnumerable<RuleResult> ratchetResults, string solutionDirectory)
    {
        IEnumerable<IGrouping<string, RuleResult>> fileGroups = ratchetResults
            .GroupBy(r => BaselineStore.ResolvePath(r.Rule.BaselinePath!, solutionDirectory), StringComparer.Ordinal);

        foreach (IGrouping<string, RuleResult> group in fileGroups)
            ApplyFile(request, group, solutionDirectory);
    }

    private void ApplyFile(BaselineRequest request, IGrouping<string, RuleResult> group, string solutionDirectory)
    {
        // Read + verify once (tamper throws here — init cannot distinguish tamper from corruption).
        BaselineDocument? existing = BaselineStore.TryReadDocument(group.Key);
        Dictionary<string, IReadOnlyList<BaselineEntry>> sections = MutableSections(existing);

        var rewrote = false;
        foreach (RuleResult result in group) rewrote |= ApplyRule(request, result, sections);

        // init writes nothing to a file whose every visited section was already captured. Without that arm
        // the write still lands, and since a composed file always carries the current schema version an
        // untouched legacy file would be upgraded in place while each section echoed "already captured —
        // unchanged": the one word the operator reads would be the one thing that was not true. The other
        // modes are the upgrade path and write regardless — each has something of its own to record.
        if (request.Mode == BaselineMode.Init && existing is not null && !rewrote)
        {
            output.WriteLine(WriteReport.Line(WriteOutcome.Unchanged, solutionDirectory, group.Key));
            return;
        }

        // A file that does not exist and gained nothing is not written either, in any mode: init lands
        // here only when no rule in the group could be captured, accept-reductions whenever the rule it
        // was asked to tighten was never captured — and each has just said so on its own line. A write here
        // would leave a section-less file in the tree under a "wrote" line contradicting that sentence.
        if (existing is null && !rewrote) return;

        WriteOutcome outcome = BaselineStore.Write(group.Key, new BaselineDocument(sections));
        output.WriteLine(WriteReport.Line(outcome, solutionDirectory, group.Key));
    }

    // Whether this rule rewrote its section — what tells the caller above whether the file has anything new to say.
    private bool ApplyRule(BaselineRequest request, RuleResult result, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        string ruleId = result.Rule.Id;
        // The rotted rule, refused ahead of the unbaselinable one because it is the dangerous half and the
        // two overlap: an empty subject reaches both, and only this sentence describes a rule whose forbidden
        // TARGET matched nothing — which produces no violations at all, so CurrentEntries hands back an empty
        // list rather than null and accept-reductions would intersect the captured section with nothing and
        // delete every entry in it. A dead target or among pattern beside live ones is the same harm at a smaller
        // grain: the entries only it could have matched go unmatched and would be deleted as reductions. It gates
        // both modes for the reason the incomplete-model and narrowing gates above gate both: init would
        // record debt nobody measured.
        if (!result.UnmatchedEntriesTested)
        {
            string refusal = result.SelectionMatchedNothing
                ? $"{ruleId}: cannot capture — the rule's selection matched nothing, so this run measured no "
                  + "debt for it; skipped. Fix the rule's selection, then re-run."
                : $"{ruleId}: cannot capture — {result.UntestedRole.NamedOperands(result.UntestedOperands)} "
                  + "matched no types, so this run measured only part of the rule; skipped. "
                  + $"{result.UntestedRole.ClearingInstruction()}, then re-run.";
            output.WriteLine(refusal);
            return false;
        }

        // A dead allowed pattern or membership only adds violations, so every unmatched entry is still fixed
        // debt and accept-reductions may retire it; only init is refused, because what it would capture
        // includes the violations the dead pattern itself caused.
        if (request.Mode == BaselineMode.Init && !result.MeasuredInFull)
        {
            output.WriteLine(StricterRefusal(ruleId, result));
            return false;
        }

        if (CurrentEntries(result) is not { } current)
        {
            output.WriteLine($"{ruleId}: cannot capture — the rule had an evaluation error; skipped.");
            return false;
        }

        sections.TryGetValue(ruleId, out IReadOnlyList<BaselineEntry>? existingEntries);
        return request.Mode == BaselineMode.Init
            ? InitRule(ruleId, current, existingEntries, sections)
            : AcceptReductions(ruleId, current, existingEntries, sections);
    }

    // Why init will not capture a rule stricter than written: what the dead pattern was meant to allow, or the
    // types a dead membership was meant to hold, are reported as violations, and a capture would record them
    // as debt.
    private static string StricterRefusal(string ruleId, RuleResult result)
    {
        DeadOperandRole role = result.StricterRole;
        string named = role.NamedOperands(result.StricterOperands);
        string itWas = result.StricterOperands.Count == 1 ? "it was" : "they were";
        string reported = role switch
        {
            DeadOperandRole.AllowList => $"what {itWas} meant to allow is reported as violations",
            DeadOperandRole.Membership => $"the types {itWas} meant to hold are reported as violations",
            _ => throw new ArgumentOutOfRangeException(nameof(result), role, "Not a stricter role.")
        };

        return $"{ruleId}: cannot capture — {named} matched no types, so {reported} and would be recorded as "
               + $"debt; skipped. {role.ClearingInstruction()}, then re-run.";
    }

    // init: grandfather an uncaptured rule's current state (empty = zero debt); leave captured rules be.
    private bool InitRule(
        string ruleId, IReadOnlyList<(BaselineEntry Identity, int Sites)> current,
        IReadOnlyList<BaselineEntry>? existingEntries, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        if (existingEntries is { } captured)
        {
            output.WriteLine($"{ruleId}: already captured ({captured.Count} entries) — unchanged.");
            return false;
        }

        sections[ruleId] = AsWritten(current);
        output.WriteLine($"{ruleId}: captured {current.Count} grandfathered {Plurals.Noun(current.Count, "violation")}.");
        return true;
    }

    // accept-reductions: section := section ∩ current, then each surviving entry's measure ratcheted down
    // to what this run observed. Never adds and never raises a count; reports both refusals. A violation that
    // no longer occurs is the whole point of the mode — which is why the incomplete-model gate fires long
    // before this runs, since a project that stopped loading looks exactly like a violation that stopped.
    // Recording a count on an entry that had none is a tightening too, and it is the route by which a file
    // written before the measure existed becomes a counted one.
    private bool AcceptReductions(
        string ruleId, IReadOnlyList<(BaselineEntry Identity, int Sites)> current,
        IReadOnlyList<BaselineEntry>? existingEntries, Dictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        if (existingEntries is not { } captured)
        {
            output.WriteLine($"{ruleId}: no baseline section — run 'sliderule baseline init' first.");
            return false;
        }

        // Keyed by identity, so what this run observed under a captured entry is one lookup away.
        // CurrentEntries folded duplicate identities, so the keys are unique.
        Dictionary<BaselineEntry, int> observed = current.ToDictionary(entry => entry.Identity, entry => entry.Sites);

        // The section as check reads it, so which item a project-wide entry stands in for is RuleBaseline.TryMatch's
        // call here, as on the verdict path, rather than a second test that agrees until one of them moves.
        var section = new RuleBaseline(captured);
        ILookup<BaselineEntry, (BaselineEntry Identity, int Sites)> standIns = current
            .Select(item =>
                (Item: item, Stored: section.TryMatch(item.Identity, out BaselineEntry? stored) ? stored : null))
            .Where(match => match.Stored is not null && !match.Stored.Equals(match.Item.Identity))
            .ToLookup(match => match.Stored!, match => match.Item);

        var kept = new List<BaselineEntry>();
        var covered = new HashSet<BaselineEntry>();
        var removed = 0;
        var lowered = 0;
        var recorded = 0;
        var grew = 0;
        var split = 0;
        foreach (BaselineEntry entry in captured)
        {
            // A project-wide entry from before items were keyed apart becomes one counted entry per item it
            // grandfathers now, each keeping its reason, so the next package or framework the project takes is
            // red. Items that already carry an entry of their own keep that one.
            if (standIns.Contains(entry))
            {
                foreach ((BaselineEntry identity, int itemSites) in standIns[entry])
                {
                    BaselineEntry counted = Counted(identity, itemSites);
                    kept.Add(entry.Because is { } because ? counted.WithBecause(because) : counted);
                    covered.Add(identity);
                }

                split++;
                continue;
            }

            // Identity intersection first: an entry no current violation matches is the reduction being
            // accepted, and it does not survive into the section.
            if (!observed.TryGetValue(entry, out int sites))
            {
                removed++;
                continue;
            }

            // The same owner the verdict path reads, so what this mode may tighten and what 'check' counts
            // as tightenable are one discrimination rather than two that agree until one of them moves.
            switch (BaselineRatchet.Classify(entry, sites))
            {
                case RatchetState.Uncounted:
                    kept.Add(entry.WithSiteCount(sites));
                    recorded++;
                    break;
                case RatchetState.Shrunk:
                    kept.Add(entry.WithSiteCount(sites));
                    lowered++;
                    break;
                case RatchetState.Grown:
                    // Stored untouched, and the mode's direction is the whole reason: raising a count is
                    // growth, and growth is attributed or it does not happen.
                    grew++;
                    kept.Add(entry);
                    break;
                case RatchetState.NotMeasured:
                case RatchetState.Held:
                    kept.Add(entry);
                    break;
            }
        }

        int additions = current.Count(item => !section.TryMatch(item.Identity, out _));

        sections[ruleId] = kept;
        // "nothing to accept" is the whole mode's verdict, so every kind of acceptance clears it: a run that
        // lowered or first recorded a count tightened the baseline as surely as one that removed an entry.
        if (removed + lowered + recorded + split == 0) output.WriteLine($"{ruleId}: nothing to accept.");
        if (removed > 0)
            output.WriteLine($"{ruleId}: accepted {Plurals.Counted(removed, "reduction")}.");
        if (lowered > 0)
            output.WriteLine($"{ruleId}: lowered the site count on {Plurals.Counted(lowered, "entry")}.");
        if (recorded > 0)
            output.WriteLine($"{ruleId}: recorded the site count on {Plurals.Counted(recorded, "entry")}.");
        if (split > 0)
            output.WriteLine(
                $"{ruleId}: split {Plurals.Counted(split, "project-wide entry")} into {Plurals.Counted(covered.Count, "entry")}, one per package or framework.");
        if (additions > 0)
            output.WriteLine(
                $"{ruleId}: refused {Plurals.Counted(additions, "addition")} — a captured baseline grows only via 'sliderule baseline add', one attributed entry at a time.");
        if (grew > 0)
            output.WriteLine(
                $"{ruleId}: refused site growth on {Plurals.Counted(grew, "entry")} — a grandfathered pair grows only via 'sliderule baseline add', one attributed entry at a time.");

        return true;
    }

    // A ratcheted rule's current violations as (identity, sites observed under it) from an empty-baseline
    // check, or null when the rule is unbaselinable: any EmptySubject/RuleError violation makes the whole
    // rule so (its identity is not stable). The count is max-folded across duplicates: a symbol ID names a
    // name rather than a node (GRAMMAR §4.3), so two violations can share one identity, and the allowance a
    // write records has to cover the larger of them. Folding is also what collapses those duplicates to the
    // single line the file has always meant them to be. The raw count rather than a Counted entry, because
    // zero sites and no measure are different questions and only BaselineRatchet may answer the second.
    private static IReadOnlyList<(BaselineEntry Identity, int Sites)>? CurrentEntries(RuleResult result)
    {
        var observed = new Dictionary<BaselineEntry, int>();
        var order = new List<BaselineEntry>();

        foreach (Violation violation in result.Violations)
        {
            BaselineEntry? identity = violation.BaselineIdentity();
            if (identity is null) return null;

            if (!observed.TryGetValue(identity, out int sites)) order.Add(identity);
            observed[identity] = Math.Max(sites, violation.Sites.Count);
        }

        return order.Select(identity => (Identity: identity, Sites: observed[identity])).ToList();
    }

    // What init writes for one rule's empty-baseline result once ApplyRule's refusals have passed: each current
    // identity, duplicates max-folded, as a write records it; null where the rule is unbaselinable. Internal so the
    // ratchet rows capture what init would write without a file.
    internal static IReadOnlyList<BaselineEntry>? CapturedEntries(RuleResult result)
    {
        IReadOnlyList<(BaselineEntry Identity, int Sites)>? current = CurrentEntries(result);
        return current is null ? null : AsWritten(current);
    }

    // The current entries as a write records each: the one composition init and CapturedEntries share.
    private static List<BaselineEntry> AsWritten(IReadOnlyList<(BaselineEntry Identity, int Sites)> current)
    {
        return current.Select(observed => Counted(observed.Identity, observed.Sites))
            .ToList();
    }

    // The entry as a write records it: an edge entry carrying the sites observed under its identity, or the
    // bare identity where there is no measure to take. A subject entry never carries one — its sites are
    // declarations (GRAMMAR §4.3) — and neither does an edge whose evidence carries no file:line at all,
    // which is the same state as an entry written before the measure existed: grandfathered at pair grain.
    private static BaselineEntry Counted(BaselineEntry identity, int sites)
    {
        return identity.IsEdge && sites > 0 ? identity.WithSiteCount(sites) : identity;
    }
}
