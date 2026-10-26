using System.Runtime.CompilerServices;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Checking;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Roslyn.Solutions;
using Xunit;

namespace SlideRule.Xunit;

/// <summary>
///     The xUnit adapter: derive a sealed class from <c>ArchRuleTests&lt;YourSpec&gt;</c> in your test
///     project, override <see cref="SolutionPath" /> to name the solution to check, and every rule in
///     the spec runs as its own named test. The rule ID is the test's display name (a scope's rules are
///     named <c>{scope-id}/containment</c> and <c>{scope-id}/tripwire</c>), so a broken architecture
///     rule reads as a failing test in the test explorer.
///     <see cref="SolutionPath" /> is the only required override.
/// </summary>
/// <remarks>
///     The adapter never builds and never restores: restore the target solution first. It reads the
///     code from source, built or not, and runs the spec your test project's build compiled. It needs
///     a .NET SDK on the test host, since it loads the solution through MSBuild, and it registers
///     MSBuild itself, so your test project needs no <c>[ModuleInitializer]</c> of its own.
/// </remarks>
/// <typeparam name="TSpec">The architecture spec to check — must be default-constructible.</typeparam>
public abstract class ArchRuleTests<TSpec> where TSpec : IArchitectureSpec, new()
{
    /// <summary>
    ///     Gets the solution to check the spec against: a <c>.sln</c> or <c>.slnx</c> file, or a
    ///     <c>.slnf</c> filter over one, which checks the projects the filter selects plus everything they
    ///     reference; <see cref="Workspace_LoadedCompletely" /> then skips, and so does any rule whose whole
    ///     subject the filter left out. The only required override. A relative path resolves against the
    ///     test process's current directory, which is not where a solution normally sits, so
    ///     <see cref="FindSolutionUp" /> covers the usual case where the solution is not copied to the test
    ///     output.
    /// </summary>
    protected abstract string SolutionPath { get; }

    /// <summary>
    ///     Gets the name of the spec's own project, which is left out of the code being checked along with
    ///     any project only it pulls in — a rule pack, say, or a helper library the solution does not
    ///     declare. Projects the solution file declares stay in even when the spec references them, so a
    ///     spec may govern the very code it compiles against. Defaults to the spec assembly's name;
    ///     override it to <see langword="null" /> when the spec lives outside the checked solution.
    /// </summary>
    protected virtual string? ExcludeProjectName => typeof(TSpec).Assembly.GetName().Name;

    /// <summary>
    ///     Gets whether the rule tests may report verdicts from a solution that did not load completely —
    ///     the adapter's form of the CLI's <c>--allow-workspace-diagnostics</c>. False by default: a load
    ///     failure fails <see cref="Workspace_LoadedCompletely" /> and every rule case skips, because a
    ///     rule measured over a codebase missing whole projects reports a verdict it never reached.
    ///     Override it to <see langword="true" /> to take the verdicts the partial model does support;
    ///     <see cref="Workspace_LoadedCompletely" /> then skips, still naming the projects that failed,
    ///     rather than pass under a name the run cannot vouch for.
    /// </summary>
    protected virtual bool AllowWorkspaceDiagnostics => false;

    /// <summary>
    ///     Resolves a solution file's absolute path by name, for the usual case where the solution is not
    ///     copied to the test output directory. Climbs from <see cref="AppContext.BaseDirectory" /> through
    ///     its ancestors, the start directory included, and returns the full path of the first one holding
    ///     a file named <paramref name="fileName" />, so a consumer writes
    ///     <c>SolutionPath =&gt; FindSolutionUp("MyApp.slnx")</c> rather than hand-rolling the climb.
    /// </summary>
    /// <param name="fileName">The solution file name to locate (for example, <c>MyApp.slnx</c>).</param>
    /// <returns>The absolute path to the located file.</returns>
    /// <exception cref="FileNotFoundException">
    ///     No ancestor of <see cref="AppContext.BaseDirectory" /> holds a file named
    ///     <paramref name="fileName" />; the message names both the file and the start directory.
    /// </exception>
    protected static string FindSolutionUp(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{fileName}' walking up from '{AppContext.BaseDirectory}'.", fileName);
    }

    /// <summary>
    ///     The row source behind <see cref="Rule_Holds" />: one row per rule in the spec, each carrying
    ///     the rule ID that becomes the test's display name. Runs at test discovery and builds the model
    ///     from the spec alone, so the test explorer can list the rules without loading a solution. The
    ///     solution is then loaded and checked once for each spec type, solution and excluded project, when
    ///     the first case naming that combination executes, and every rule case naming it reads its verdict
    ///     from that one run, so two classes over one spec that name different solutions each check their
    ///     own. A spec that fails to build collapses to a single row, which fails when the run reaches it
    ///     and carries the spec's own errors. A test project that loaded a version of SlideRule other than the
    ///     adapter's own gets a single skipped row instead, whose reason names both versions. The test
    ///     framework calls this; there is nothing to override.
    /// </summary>
    // The version check must run before the JIT compiles anything that names the contract's engine: a method's
    // references bind when it is compiled, so a member a mismatched copy moved throws at that method's caller,
    // ahead of any check inside it. RuleRows and Rule_Holds therefore hold the check and one call each, and the
    // engine sits behind never-inlined helpers. Workspace_LoadedCompletely names none of it, and must stay so.
    public static IEnumerable<ITheoryDataRow> RuleRows()
    {
        if (ContractVersion.Mismatch() is { } mismatch)
            return
            [
                new TheoryDataRow<string>(string.Empty)
                {
                    TestDisplayName = $"{typeof(TSpec).Name}: not checked",
                    Skip = mismatch
                }
            ];

        return ModelRows();
    }

    // Never inlined: see the comment on RuleRows. The sentinel row lands a spec-build failure red at run
    // time — where the pipeline's rebuild rethrows the real SpecValidationException — rather than as a
    // discovery diagnostic no runner surfaces.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IEnumerable<ITheoryDataRow> ModelRows()
    {
        ArchitectureModel model;
        try
        {
            model = ArchModelBuilder.Build(new TSpec());
        }
        catch
        {
            return
            [
                new TheoryDataRow<string>(string.Empty) { TestDisplayName = $"{typeof(TSpec).Name}: spec build failed" }
            ];
        }

        return model.Rules.Select(ITheoryDataRow (rule) =>
            new TheoryDataRow<string>(rule.Id) { TestDisplayName = rule.Id });
    }

    /// <summary>
    ///     One rule's verdict, the test named by its rule ID: it passes when the rule holds, fails with
    ///     the block <c>sliderule check</c> prints for it (the reason, the fix, and every violation
    ///     with its file and line) when it does not, and skips when the run reached no verdict, carrying
    ///     the reason. A scope's tripwire is the usual skip: it has no diff base to compare changed files
    ///     against, so a <c>Caution</c> scope, whose tripwire is its only rule, is a case that is always
    ///     skipped and never fires. Rows come from <see cref="RuleRows" />.
    /// </summary>
    [Theory]
    [MemberData(nameof(RuleRows))]
    public async Task Rule_Holds(string ruleId)
    {
        if (ContractVersion.Mismatch() is { } mismatch) Assert.Skip(mismatch);

        await VerdictAsync(ruleId);
    }

    // Never inlined: see the comment on RuleRows.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task VerdictAsync(string ruleId)
    {
        ArchCheckRun run = await GetRunAsync();

        if (!run.ResultsById.TryGetValue(ruleId, out RuleResult? result))
            throw new InvalidOperationException(
                $"Rule '{ruleId}' was enumerated at discovery but is absent from the check run.");

        if (run.Diagnostics.Gates(AllowWorkspaceDiagnostics))
            Assert.Skip(IncompleteModelGate.AdapterSkipReason);

        switch (result.Status)
        {
            case RuleStatus.Skipped:
                Assert.Skip(result.SkipReason ?? "rule skipped");
                break;
            case RuleStatus.Failed:
                Assert.Fail(HumanReportRenderer.RuleBlock(result, run.SolutionDirectory));
                break;
        }
    }

    /// <summary>
    ///     The named answer to a solution that did not load completely. It fails, naming the projects that
    ///     failed to load, and every rule case then skips rather than report a verdict reached over a
    ///     partial model. It skips, naming the same projects, when
    ///     <see cref="AllowWorkspaceDiagnostics" /> opted into checking the model as it loaded, and it
    ///     passes silently on a clean load. It also skips on the two runs that are smaller rather than
    ///     wrong (one through a <c>.slnf</c> solution filter, one over a solution declaring projects the
    ///     checker cannot read), naming what went unchecked, because a pass here would claim the whole
    ///     solution was checked. A solution can be both at once, and then it reports one block for each; a
    ///     solution that failed to load outranks both and is reported alone. Before any of that it fails, naming
    ///     both versions, when the test project loaded a version of SlideRule other than the adapter's own,
    ///     and a single skipped case named after the spec replaces the rule cases: keep the two packages on one
    ///     version.
    /// </summary>
    [Fact]
    public async Task Workspace_LoadedCompletely()
    {
        if (ContractVersion.Mismatch() is { } mismatch) Assert.Fail(mismatch);

        ArchCheckRun run = await GetRunAsync();
        if (run.Diagnostics.IsIncomplete)
        {
            if (AllowWorkspaceDiagnostics) Assert.Skip(IncompleteModelGate.AdapterOptedIn(run.Diagnostics));
            Assert.Fail(IncompleteModelGate.AdapterRefusal(run.Diagnostics));
        }

        // Only ever a clean load by here: a broken model outranks a small one, and it has already answered.
        var blocks = new List<string>();

        if (run.Diagnostics.UncheckedProjects.Count > 0)
            blocks.Add(
                NarrowedUniverseNotice.AdapterSkip(
                    Path.GetFileName(run.SolutionPath),
                    NarrowedUniverseNotice.Relative(run.Diagnostics.UncheckedProjects, run.SolutionDirectory)));

        if (run.Diagnostics.UnsupportedProjects.Count > 0)
            blocks.Add(
                UnsupportedProjectsNotice.AdapterSkip(
                    UnsupportedProjectsNotice.Relative(run.Diagnostics.UnsupportedProjects, run.SolutionDirectory)));

        if (blocks.Count > 0) Assert.Skip(string.Join("\n", blocks));
    }

    // Lazily start (and then share) this TSpec's check run for one SolutionPath/ExcludeProjectName pair: classes
    // over one spec that name the same pair share a run, and AllowWorkspaceDiagnostics, read per case, is the
    // one hook that may differ between them. A cached faulted task rethrows on every case (e.g. a spec-build or
    // workspace failure surfaces identically on every rule).
    private Task<ArchCheckRun> GetRunAsync()
    {
        (string SolutionPath, string? ExcludeProjectName) key = (Path.GetFullPath(SolutionPath), ExcludeProjectName);
        lock (Gate)
        {
            if (!Runs.TryGetValue(key, out Task<ArchCheckRun>? run))
            {
                run = RunPipelineAsync(key.SolutionPath, key.ExcludeProjectName);
                Runs.Add(key, run);
            }

            return run;
        }
    }

    // NoInlining + the non-Roslyn EnsureMsBuild() first: keeps the JIT from resolving MSBuildWorkspace
    // before MSBuildLocator registration, so a consumer needs no ModuleInitializer. The check itself is the
    // shared ArchCheckSequence, so the adapter and the CLI cannot disagree about it; what the adapter brings
    // is its own extraction — a cold one-shot workspace, opened inside the delegate so the sequence's
    // baselines-before-extraction ordering covers the load too.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<ArchCheckRun> RunPipelineAsync(string solutionPath, string? excludeProjectName)
    {
        EnsureMsBuild();

        ArchitectureModel model = ArchModelBuilder.Build(new TSpec());
        string fullSolutionPath = Path.GetFullPath(solutionPath);
        string solutionDirectory = SolutionProjectFileParser.AnchorDirectory(fullSolutionPath);

        var diagnostics = new List<string>();
        LoadedSolution? opened = null;
        // Escapes the extraction delegate the way `opened` does: its facts exist only once a model has been
        // merged, inside the delegate. The checker and the completeness test read this one composition, so they
        // cannot describe the same load differently. The seed is never read: the sequence invokes the delegate
        // exactly once.
        WorkspaceDiagnostics loadDiagnostics = WorkspaceDiagnostics.None;
        try
        {
            CheckReport report = await ArchCheckSequence.ExecuteAsync(
                model, model.Rules, fullSolutionPath, solutionDirectory,
                async ct =>
                {
                    LoadedSolution loaded = await WorkspaceLoader.LoadAsync(fullSolutionPath, diagnostics.Add, ct);
                    opened = loaded; // handed out so the finally below disposes it whatever the walk does

                    // The same exclusion closure the CLI applies, from one read of the solution's declared members.
                    IReadOnlySet<string>? declaredMembers = SpecExclusion.TryReadDeclaredMembers(fullSolutionPath);
                    IReadOnlyCollection<string>? exclude = excludeProjectName is null
                        ? null
                        : SpecExclusion.Compute(loaded.Solution, declaredMembers, excludeProjectName);
                    CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(
                        loaded.Solution, exclude, loaded.TargetFrameworks, declaredMembers, ct);
                    // The merge notes and multi-targeted projects go unrendered here, but the record fills them as
                    // one pair from one read.
                    var composed = new WorkspaceDiagnostics(
                        diagnostics, codebase.MergeNotes, loaded.FailedProjects, loaded.UncheckedProjects,
                        loaded.RestoreFailedProjects, loaded.UnsupportedProjects,
                        MultiTargetedProjects.Of(codebase));
                    loadDiagnostics = composed;
                    return new ExtractedCodebase(codebase, composed);
                },
                null, CancellationToken.None);

            Dictionary<string, RuleResult> byId = report.Results.ToDictionary(r => r.Rule.Id, r => r, StringComparer.Ordinal);
            return new ArchCheckRun(byId, solutionDirectory, fullSolutionPath, loadDiagnostics);
        }
        finally
        {
            opened?.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureMsBuild()
    {
        MsBuildBootstrap.EnsureInitialized();
    }

    private sealed record ArchCheckRun(
        IReadOnlyDictionary<string, RuleResult> ResultsById,
        string SolutionDirectory,
        string SolutionPath,
        WorkspaceDiagnostics Diagnostics);

    // Per-closed-generic statics are essential: each ArchRuleTests<TSpec> caches its own spec's check runs, one
    // per solution and excluded project, so these must not be shared across TSpec.
    // ReSharper disable StaticMemberInGenericType
    private static readonly Lock Gate = new();

    private static readonly Dictionary<(string SolutionPath, string? ExcludeProjectName), Task<ArchCheckRun>> Runs = [];
    // ReSharper restore StaticMemberInGenericType
}
