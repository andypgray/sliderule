using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Rendering;
using SlideRule.Roslyn.Baselines;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Roslyn.Checking;

/// <summary>
///     The check sequence, in one place: load the ratcheted baselines, resolve the optional diff context,
///     extract the codebase, evaluate the rules. A host that runs it gets that ordering rather than restating it.
/// </summary>
/// <remarks>
///     A tampered baseline file, and a <c>--diff-base</c> ref that does not resolve, must fail the run before
///     the Roslyn walk it would otherwise pay for. Extraction and the diff are delegates for that reason: the
///     ordering then holds whatever the caller's extraction does, a cold workspace load included.
/// </remarks>
internal static class ArchCheckSequence
{
    /// <summary>
    ///     Runs the sequence over <paramref name="rules" />, the whole model's or a narrowed selection, and returns
    ///     the report.
    /// </summary>
    /// <remarks>
    ///     <paramref name="solutionPath" /> is the solution or the <c>.slnf</c> the run was pointed at; its file
    ///     name is what a narrowing skip names. A null <paramref name="resolveDiff" /> means the run has no diff
    ///     base, and its scope tripwires skip.
    /// </remarks>
    internal static async Task<CheckReport> ExecuteAsync(
        ArchitectureModel model,
        IReadOnlyList<ArchRule> rules,
        string solutionPath,
        string solutionDirectory,
        Func<CancellationToken, Task<ExtractedCodebase>> extract,
        Func<CancellationToken, Task<DiffContext>>? resolveDiff,
        CancellationToken ct)
    {
        BaselineIndex baselines = BaselineStore.LoadForModel(model, solutionDirectory);

        DiffContext? diff = resolveDiff is null ? null : await resolveDiff(ct).ConfigureAwait(false);

        ExtractedCodebase extracted = await extract(ct).ConfigureAwait(false);
        NarrowedUniverse? narrowing = Narrowing(solutionPath, extracted.Diagnostics.UncheckedProjects);
        IncompleteModel? incomplete = Incomplete(extracted.Diagnostics);
        UnsurveyedProjects? unsurveyed = Unsurveyed(extracted.Diagnostics.UnsupportedProjects, solutionDirectory);

        var caveats = new ModelCaveats(incomplete, unsurveyed, narrowing);
        return ArchChecker.Check(rules, extracted.Codebase, baselines, diff, caveats, new SolutionRoot(solutionDirectory), ct);
    }

    // The values below are facts about the load, worded here as sentences because Core cannot name a project
    // path. Null unless the run checked less than the solution declares, measured from the load and never read
    // off the filter text (ProjectLoadFailures' remarks say why).
    private static NarrowedUniverse? Narrowing(string solutionPath, IReadOnlyList<string> uncheckedProjects)
    {
        if (uncheckedProjects.Count == 0) return null;

        string filterName = Path.GetFileName(solutionPath);
        string skipReason = NarrowedUniverseNotice.RuleSkipReason(filterName, uncheckedProjects.Count);
        string inertTargetHint = NarrowedUniverseNotice.InertTargetHint(filterName, uncheckedProjects.Count);
        return new NarrowedUniverse(skipReason, inertTargetHint);
    }

    // Keyed by the name a selection would give each project. Two entries under one name take the first's cure,
    // since either names the project the selection is anchored on.
    private static UnsurveyedProjects? Unsurveyed(
        IReadOnlyList<UnsupportedProject> unsupportedProjects, string solutionDirectory)
    {
        if (unsupportedProjects.Count == 0) return null;

        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        var cureByProject = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (UnsupportedProject project in unsupportedProjects)
            cureByProject.TryAdd(
                UnsupportedProjectsNotice.SelectionName(project),
                UnsupportedProjectsNotice.UnsurveyedCure(project, relativizer));
        return new UnsurveyedProjects(cureByProject);
    }

    // Not gated on the operator's opt-in to a partial model: that changes the exit code, never the truth about
    // the model, and a rule whose selection matched nothing is owed the cure either way.
    private static IncompleteModel? Incomplete(WorkspaceDiagnostics diagnostics)
    {
        return diagnostics.IsIncomplete
            ? new IncompleteModel(IncompleteModelGate.EmptySelectionHint(diagnostics))
            : null;
    }
}
