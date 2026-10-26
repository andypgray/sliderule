using SlideRule.Checking;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Hosting;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>check</c> verb over the shared <see cref="CheckPipeline" />: it renders human, JSON or the hook
///     document, and exits 0 clean or 1 on a red rule; grandfathered violations do not fail the run. A run
///     narrowed by <see cref="CheckRequest.Rules" /> is a smaller report of the same shape and the same exit
///     contract, so its verdict answers a smaller question — which is what the stamp and the document's
///     <c>rulesFilter</c> say out loud.
/// </summary>
/// <remarks>
///     <b>What it refuses.</b> A workspace-load failure overrides that verdict: the model is incomplete,
///     so <c>check</c> fails closed with exit 2 unless
///     <see cref="CheckRequest.AllowWorkspaceDiagnostics" /> was passed — a rule that "passes" only
///     because a project did not load is worse than no answer. The decision reads which projects failed
///     to load or restore, never diagnostic text, and the messages every verb uses for the condition live
///     in <see cref="IncompleteModelGate" />; <c>check</c> renders before the gate fires, so the verdict
///     also rides the JSON document and the SARIF stamp. Expected failures surface as
///     <see cref="UserErrorException" />; the top-level handler maps them to exit 2.
/// </remarks>
internal sealed class CheckRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource source,
    IEnvironment environment,
    IResponseFitter? fitter = null) : CacheWiredRunner(source, environment, fitter)
{
    /// <summary>
    ///     Runs the check and writes it, on the channels <paramref name="request" /> asks for. Ordinary
    ///     runs stream to the real writers; <see cref="CheckRequest.HookJson" /> composes into one buffer
    ///     first, because what a hook may write to stdout depends on a verdict that does not exist until
    ///     after the stamps and the diagnostics have been written.
    /// </summary>
    public async Task<int> RunAsync(CheckRequest request, CancellationToken ct)
    {
        if (!request.HookJson) return (await ExecuteAsync(request, output, error, ct)).Code;

        // The platform's own line terminator, deliberately: everything but the clean-with-warnings case is
        // flushed verbatim, and a buffer that normalized would make hook mode change the bytes of a report it
        // has no business touching.
        var composed = new StringWriter();
        (int code, int warnings) = await ExecuteAsync(request, composed, composed, ct);
        WriteHookOutput(composed.ToString(), code, warnings, request.HookEvent ?? HookReportRenderer.DefaultEvent);

        return code;
    }

    // Ahead of the load and of the stamps, because a shape check needs neither the spec model nor the
    // codebase, and a narrowing preamble above a refusal qualifies nothing. Genuine ref validation cannot
    // move up here — git runs in the solution directory the load resolves — so a ref that does not
    // exist stays GitChangedFiles' answer to give.
    private static void RefuseAStringifiedArrayDiffBase(string diffBase)
    {
        var lead = $"Cannot resolve changed files since '{diffBase}'";
        Refusals.RefuseSingleValue(diffBase, lead, "pass one git ref");
    }

    // The check proper, over whichever pair of channels the caller handed it: the report and the human
    // stamps to `stdout`, the workspace diagnostics and the incomplete-model refusal to `stderr`. The
    // warning count rides back beside the exit code because hook mode needs both to decide what to write,
    // and the report itself is gone by the time it decides.
    private async Task<(int Code, int Warnings)> ExecuteAsync(
        CheckRequest request, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (request.DiffBase is { } diffBase) RefuseAStringifiedArrayDiffBase(diffBase);

        TextWriter human = HumanChannel(request.Json, stdout);

        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, request.Solution, request.Spec, request.WorkingDirectory, request.NoCache, ct);

        // Select — and refuse an unmatched filter — before extraction: rule IDs come from the spec model,
        // which is already resolved here, so nothing about a bad --rules value needs a codebase walk to say
        // so. The stamp goes out just as early, because it says what the operator is about to wait for.
        IReadOnlyList<string> ruleGlobs = GlobList.Parse(request.Rules);
        IReadOnlyList<ArchRule> rules = CheckPipeline.SelectRules(source.Model, ruleGlobs);

        // All three stamps are human-channel only: under --json the document carries the same facts in
        // uncheckedProjects, unsupportedProjects and rulesFilter.
        NarrowingNotices.Stamp(human, source, NarrowedUniverseNotice.CheckStamp);
        UnsupportedProjectsNotices.Stamp(human, source, UnsupportedProjectsNotice.CheckStamp);
        WriteFilterStamp(human, ruleGlobs, rules.Count, source.Model.Rules.Count);

        CheckReport report = await CheckPipeline.ExecuteAsync(source, request.DiffBase, request.ChangedFiles, rules, ct);
        RecordCacheOutcome(source);

        WorkspaceDiagnostics diagnostics = source.Diagnostics;

        // Fail closed on an incomplete model (a project failed to load, or to restore): exit 2 takes precedence
        // over 0/1 unless the operator opted into the partial model, and no diagnostic text is an input to that.
        // Computed above Render, which reads the same value, so the document and the SARIF stamp carry the
        // verdict the gate below returns.
        bool gated = diagnostics.Gates(request.AllowWorkspaceDiagnostics);

        Render(request, stdout, human, stderr, report, source, !gated, ruleGlobs);

        // The incomplete-model gate: exit 2 overrides the 0/1 verdict. SARIF (if requested) was already
        // written above with executionSuccessful: false, so the gate verdict still reaches code scanning.
        if (IncompleteModelNotices.Refused(
                stderr, diagnostics, request.AllowWorkspaceDiagnostics, IncompleteModelGate.CheckMessage))
            return (ExitCodes.Error, report.WarningCount);

        return (report.HasViolations ? ExitCodes.Violations : ExitCodes.Clean, report.WarningCount);
    }

    // Hook mode's whole observable. A clean run (exit 0) with something to say hands the composed report to
    // the agent as additional context for the event the request names, which is the one exit-0 channel
    // Claude Code turns into a transcript message; a clean run with nothing to say writes nothing, because a
    // hook that speaks whenever it runs is a hook people turn off. Every other exit code writes the report
    // verbatim — that is what the hook puts on stderr to block with, and it is why the composed text is
    // not the JSON's only home. The context case must stay the one Render composes without its pass blocks.
    private void WriteHookOutput(string composed, int exitCode, int warnings, string hookEvent)
    {
        if (exitCode != ExitCodes.Clean)
        {
            output.Write(composed);
            return;
        }

        if (warnings == 0) return;

        output.Write(SlideRuleJson.Terminated(HookReportRenderer.Document(composed.TrimEnd('\r', '\n'), hookEvent)));
    }

    // The human filter stamp, written by the runner rather than by Core's shared HumanReportRenderer so an
    // unfiltered run writes that renderer's output alone and the xUnit adapter never sees a CLI concern.
    // It says what the report cannot: how much of the spec this verdict covers.
    private static void WriteFilterStamp(
        TextWriter human, IReadOnlyList<string> ruleGlobs, int selected, int total)
    {
        if (ruleGlobs.Count == 0) return;

        human.WriteLine(
            $"Checking {selected} of {total} rules matching '{string.Join(";", ruleGlobs)}'; the verdict below "
            + "covers only those, so a clean result here is not a clean solution.");
        human.WriteLine();
    }

    private void Render(
        CheckRequest request, TextWriter stdout, TextWriter human, TextWriter stderr, CheckReport report,
        CodebaseSource source, bool executionSuccessful, IReadOnlyList<string> ruleGlobs)
    {
        // check is the one verb that folds the advisory merge notes into its rendered diagnostics stream
        // (stderr warning: lines + the JSON workspaceDiagnostics array + SARIF notifications), load failures
        // first, because it is the verb that prints the violations a merge advisory can explain. The NuGet
        // audit advisories are not in that stream: they render apart, grouped, on all three surfaces. Neither
        // reaches the gate: that decision is WorkspaceDiagnostics' and keys on the blamed projects alone.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        IReadOnlyList<string> renderedDiagnostics = diagnostics.RenderedWithMergeNotes;

        // The document's trust stamp, composed here for the same reason the stream above is: grouping the
        // audit advisories and relativizing the project lists are the load's work, not a rung's, and the
        // grain ladder below renders the same load once per rung. stderr reads its advisories off it too, so
        // the terminal and the document cannot disagree about how they group.
        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(diagnostics, source.SolutionDirectory);

        // --json purity: only the JSON document reaches stdout; diagnostics go to stderr and ride inside the
        // document, the NuGet audit advisories in packageAudit and the rest in workspaceDiagnostics.
        WorkspaceDiagnosticsRenderer.Render(stderr, trust, renderedDiagnostics, request.Json);

        if (request.Json)
            WriteJson(request, stdout, report, source, renderedDiagnostics, trust, ruleGlobs);
        else if (request.HookJson && executionSuccessful && !report.HasViolations && report.WarningCount > 0)
            // Exactly the run WriteHookOutput hands the agent as context (exit 0 with a warning), and the one
            // report hook mode may reshape, because it is the one it does not flush verbatim. The agent holds
            // the rule board already, and a pass block per rule is what pushed this repository's own context
            // past the client's inline limit, where the preview it got was pass lines and nothing else.
            HumanReportRenderer.RenderWithoutPasses(stdout, report, source.SolutionDirectory);
        else
            HumanReportRenderer.Render(stdout, report, source.SolutionDirectory);

        // The optional third render target: a SARIF file alongside stdout. The wrote line is human-mode only
        // (it would break --json stdout purity); the SARIF itself carries the same result model either way.
        if (request.Sarif is { } sarifPath)
        {
            SarifReportRenderer.Render(
                sarifPath, report, source.SolutionDirectory, executionSuccessful, renderedDiagnostics, trust);
            human.WriteLine($"wrote {PathFormat.Relative(source.SolutionDirectory, sarifPath)}");
        }
    }

    // The JSON report, each rung re-composed from the result model already in hand, no second check.
    // Degrading coarsens the grain and never touches the selection: every rule the run evaluated is still
    // here with its verdict, and the summary counts still cover exactly what ran. The two renderings arrive
    // composed because stderr, the document and SARIF must read one composition.
    private void WriteJson(
        CheckRequest request, TextWriter stdout, CheckReport report, CodebaseSource source,
        IReadOnlyList<string> renderedDiagnostics, WorkspaceTrustStamp trust, IReadOnlyList<string> ruleGlobs)
    {
        string specAssembly = Path.GetFileName(source.Resolution.DllPath);
        WriteFitted(stdout, request.Grain, at => JsonReportRenderer.Document(
            report, source.SolutionDirectory, source.SolutionName, specAssembly, request.DiffBase,
            renderedDiagnostics, trust, ruleGlobs, at));
    }
}
