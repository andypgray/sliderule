using SlideRule.Checking;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Hosting;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>status</c> verb: the burndown over the shared <see cref="CheckPipeline" />, human or JSON. Unlike
///     <c>check</c>, status <em>reports</em> — it exits 0 even with red rules; only an error (a tampered baseline,
///     an unresolvable spec) exits 2 via the top-level handler.
/// </summary>
/// <remarks>
///     <b>What it refuses.</b> The one thing status gates on is the model not being the codebase: a
///     project that failed to load declares no types, so every burndown count reads low and a ratchet
///     read against it looks like progress that never happened. That fails closed on <c>check</c>'s
///     terms — the burndown still renders, then exit 2, opt-out
///     <see cref="StatusRequest.AllowWorkspaceDiagnostics" /> (<see cref="IncompleteModelGate" />).
/// </remarks>
internal sealed class StatusRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource source,
    IEnvironment environment) : CacheWiredRunner(source, environment)
{
    public async Task<int> RunAsync(StatusRequest request, CancellationToken ct)
    {
        // This run's human channel. --json owns stdout, where the burndown document is the only thing
        // written, so under it the stamp below goes nowhere.
        TextWriter human = request.Json ? TextWriter.Null : output;

        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, request.Solution, request.Spec, request.WorkingDirectory, request.NoCache, ct);

        // status carries no --rules flag: a burndown of part of the spec would read as progress on all of
        // it. The empty selection — every rule — is spelled out so every caller of the shared pipeline
        // chooses its rules the same way.
        IReadOnlyList<ArchRule> rules = CheckPipeline.SelectRules(source.Model, []);

        CheckReport report = await CheckPipeline.ExecuteAsync(source, null, null, rules, ct);
        RecordCacheOutcome(source);

        // Composed once and handed to both surfaces, so the MSBuild-selection note rides the burndown
        // document as well as stderr.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        IReadOnlyList<string> renderedDiagnostics = diagnostics.Rendered;

        // And the stamp with it, so the advisories stderr echoes are the very entries the document carries
        // rather than a second grouping of the same load lines.
        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(diagnostics, source.SolutionDirectory);
        WorkspaceDiagnosticsRenderer.Render(error, trust, renderedDiagnostics, request.Json);

        // Both stamps are human-channel only, where the document carries the same facts in
        // uncheckedProjects and unsupportedProjects.
        NarrowingNotices.Stamp(human, source, NarrowedUniverseNotice.StatusStamp);
        UnsupportedProjectsNotices.Stamp(human, source, UnsupportedProjectsNotice.StatusStamp);

        // check's shape: render the burndown it does have, stamping the verdict into the document, then gate.
        if (request.Json)
            StatusJsonRenderer.Render(
                output, report, source.SolutionName, Path.GetFileName(source.Resolution.DllPath),
                renderedDiagnostics, trust);
        else
            foreach (string line in StatusFormatter.Lines(report))
                await output.WriteLineAsync(line);

        if (IncompleteModelNotices.Refused(
                error, diagnostics, request.AllowWorkspaceDiagnostics, IncompleteModelGate.StatusMessage))
            return ExitCodes.Error;

        return ExitCodes.Clean;
    }
}
