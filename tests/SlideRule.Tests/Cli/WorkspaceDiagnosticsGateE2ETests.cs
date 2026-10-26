using System.Text.Json;
using Shouldly;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>The workspace-diagnostics contract on <c>check</c>.</summary>
/// <remarks>
///     <para>
///         Every part runs against the real MyApp fixture.
///         Driven through <see cref="CheckRunner" /> with an injected source that wraps the real cold load
///         and adds synthetic diagnostics, synthetic failed projects, synthetic restore-failed ones and/or
///         synthetic unchecked ones — the one way to exercise the gate without a genuinely broken project,
///         and the one way to put the inputs in front of it <em>separately</em>, which is the whole subject
///         of the last three parts below.
///     </para>
/// </remarks>
public sealed class WorkspaceDiagnosticsGateE2ETests
{
    private const string LoadDiagnostic = DiagnosticInjectingSolutionSource.SimulatedLoadFailure;

    // The project the gate is told failed to load, and the only thing that makes it fire. Injected apart
    // from the diagnostic above because the product separates them: the diagnostic renders, this decides.
    private const string BrokenProject = DiagnosticInjectingSolutionSource.SimulatedFailedProject;

    // The project the run is told a solution filter left out. The third input, separate again: this one
    // neither renders as a warning nor decides anything — it scopes the answer.
    private const string UncheckedProject = "C:/repo/MyApp.Skipped/MyApp.Skipped.csproj";

    // The project the gate is told restored badly — the fourth input, and the second that decides. It loaded
    // completely, which is exactly why it needs its own slot: nothing about the loaded solution says so.
    private const string RestoreFailedProject = DiagnosticInjectingSolutionSource.SimulatedRestoreFailedProject;

    // The NU1510 pruning advisory, captured verbatim. An ordinary restore warning that says nothing about
    // whether the model built — and it refused a solution whose one rule passed, the defect the "no message
    // gates" part below exists to keep closed.
    private const string PruningAdvisory =
        CapturedAuditLines.FramePrefix + "/src/App/App.csproj" + CapturedAuditLines.FrameSeparator
        + "PackageReference System.Text.Encodings.Web will not be pruned. Consider removing this package "
        + "from your dependencies, as it is likely unnecessary.";

    // The same NU1510, in German. Nothing in the product reads either spelling.
    private const string GermanPruningAdvisory =
        CapturedAuditLines.FramePrefix + "/src/App/App.csproj" + CapturedAuditLines.FrameSeparator
        + "PackageReference System.Text.Encodings.Web wird nicht gekürzt. Erwägen Sie, dieses Paket aus "
        + "Ihren Abhängigkeiten zu entfernen, da es wahrscheinlich nicht erforderlich ist.";

    // The NU1900 audit-fetch failure in German, captured verbatim. The hardest case in the family and the
    // reason a text matcher could never be finished: no GHSA URL, no NU1900 token, and no English phrase
    // to key on.
    private const string GermanAuditFetchFailure =
        CapturedAuditLines.FramePrefix + "/src/App/App.csproj" + CapturedAuditLines.FrameSeparator
        + CapturedAuditLines.GermanAuditFetchFailure;

    // The injected advisory, held as its two halves as well as the whole line, because every surface
    // reports the halves: NuGet's message once, under packageAudit, with the name of the project the frame
    // names beside it. No output channel carries the whole line; it is what the load hands in.
    private const string AuditMessage = CapturedAuditLines.CryptographyXmlAdvisory;

    private const string AuditProject = "App";

    private static readonly string AuditDiagnostic = CapturedAuditLines.FramedFor(AuditProject, AuditMessage);

    private const string GateLine =
        "error: the model is incomplete — 1 project failed to load, so check cannot pass:";

    private const string RestoreGateLine =
        "error: the model is incomplete — NuGet packages did not resolve for 1 project, so check cannot pass: "
        + "package references that resolved to nothing produce no edges, so a rule about a package is "
        + "measured against a model that never saw it:";

    // The four injected-diagnostic runs this class reads from more than one row. Each is a read-only
    // check over one solution, one spec and one injected cause; the rows differ only in which channel
    // they assert on, so one run answers them all — the shape CheckCommandE2ETests and
    // TripwireDiffE2ETests already use. Nothing here writes, so a shared result cannot leak state, and each
    // runs uncancellable, because the row that starts it is not the only row that awaits it.
    private static readonly Lazy<Task<CliResult>> LoadDiagnosticHuman =
        new(() => RunWithInjectedDiagnosticAsync(
            CliRunner.CleanSpecDll, false, false, cancellationToken: CancellationToken.None));

    private static readonly Lazy<Task<CliResult>> LoadDiagnosticJson =
        new(() => RunWithInjectedDiagnosticAsync(
            CliRunner.CleanSpecDll, false, true, cancellationToken: CancellationToken.None));

    private static readonly Lazy<Task<CliResult>> RestoreFailureHuman =
        new(() => RunWithInjectedRestoreFailureAsync(
            CliRunner.CleanSpecDll, false, false, cancellationToken: CancellationToken.None));

    private static readonly Lazy<Task<CliResult>> AuditDiagnosticJson =
        new(() => RunWithInjectedDiagnosticAsync(
            [AuditDiagnostic], CliRunner.CleanSpecDll, false, true, cancellationToken: CancellationToken.None));

    // One colliding-type tree, both channels off it: the two rows below assert on the same run's human
    // and JSON renderings, and this class's fixture lease exists for nothing else. The lease is taken
    // and released inside the factory, as TripwireDiffE2ETests does.
    private static readonly Lazy<Task<(CliResult Human, CliResult Json)>> CollidingType = new(async () =>
    {
        using var workspace = new TempFixtureWorkspace();
        WriteCollidingType(workspace);

        CliResult human = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache");
        CliResult json = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache", "--json");

        return (human, json);
    });

    // ── Workspace-load diagnostics fail check closed ──────────────────────────────────────────────────

    [Fact]
    public async Task Check_WorkspaceLoadDiagnostic_NoFlag_FailsClosedWithExitTwoAndGateLine()
    {
        // The clean spec would exit 0, but a project failed to load — the gate overrides that verdict.
        CliResult result = await LoadDiagnosticHuman.Value;

        result.ShouldRefuseWith(
            GateLine,
            BrokenProject, // and the refusal names what failed
            $"warning: {LoadDiagnostic}"); // the load failure still prints as a warning
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnostic_WithFlag_RestoresPriorExitAndSuppressesGateLine()
    {
        // The escape hatch: the operator opts into the partial model, so the clean spec exits 0 as before.
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, true, false);

        result.ShouldSucceed();
        result.Err.ShouldContain($"warning: {LoadDiagnostic}"); // the warning still renders
        result.Err.ShouldNotContain("error: the model is incomplete"); // but the gate did not fire
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnostic_ViolatedSpecNoFlag_GateTakesPrecedenceOverExitOne()
    {
        // The violated spec would exit 1; the incomplete-model gate takes precedence and exits 2.
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.ViolatedSpecDll, false, false);

        result.ShouldRefuseWith(GateLine, BrokenProject);
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnosticSarif_RecordsExecutionUnsuccessful()
    {
        // SARIF is written before the fail-closed gate returns, so the incomplete-model verdict reaches code
        // scanning: the invocation records executionSuccessful: false and the load diagnostic rides as a
        // tool-execution notification, even though the run exits 2.
        using TempDirectory temp = TestTempRoot.Fresh("gate-sarif");
        string sarifPath = temp.PathOf("gate.sarif");

        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, false, sarifPath);

        result.ShouldRefuseWith();
        string sarif = File.ReadAllText(sarifPath);
        sarif.SarifExecutionSuccessful()
            .ShouldBeFalse(sarif);
        sarif.ShouldCarryNotification("warning", LoadDiagnostic);
    }

    // ── The MSBuild selection rides both surfaces, but only when something failed ──────────────────────

    [Fact]
    public async Task Check_WorkspaceLoadDiagnostic_NamesTheMsBuildSelectionAndTheOverrideVariable()
    {
        // "A project failed to load" is nearly always a question about which MSBuild opened it, so the
        // selection and the override variable print beside the failure.
        CliResult result = await LoadDiagnosticHuman.Value;

        result.Err.ShouldContain(
            $"warning: MSBuild for this run: {MsBuildBootstrap.LastSelection}.",
            customMessage: result.Transcript());
        result.Err.ShouldContain(
            "Set SLIDERULE_VS_INSTALL_PATH to a Visual Studio install root", customMessage: result.Transcript());
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnosticJson_LandsTheMsBuildNoteInTheDocumentBesideStderr()
    {
        // stderr is the CLI's channel and the MCP surface discards it, so the note only reaches a client if
        // it rides the document. It does, because it is composed into the one list both renderers read
        // rather than appended at write time.
        CliResult result = await LoadDiagnosticJson.Value;

        result.ShouldRefuseWith(
            "MSBuild for this run:", // the note is still on stderr, unchanged
            GateLine);
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.Strings("workspaceDiagnostics")
            .ShouldBe([LoadDiagnostic, MsBuildBootstrap.SelectionNote()]);
        // The gate line goes to stderr and never leaks onto stdout, so a hook can still parse the document.
        result.Out.ShouldNotContain("error: the model is incomplete");
    }

    [Fact]
    public async Task Check_NuGetAuditDiagnosticJson_LeavesTheStreamAndItsNoteForPackageAudit()
    {
        // The note answers which MSBuild opened a project, and an advisory raises no such question, so once
        // the advisory leaves the stream for packageAudit there is nothing for the note to follow: the run
        // carries the entry, an empty stream, and no verdict slot.
        CliResult result = await AuditDiagnosticJson.Value;

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.Strings("workspaceDiagnostics")
            .ShouldBeEmpty();
        document.ShouldCarryPackageAudit(AuditMessage, [AuditProject]);
        document.ShouldNotBeStampedIncomplete();
        result.Out.ShouldNotContain("error: the model is incomplete");
        result.Err.ShouldNotContain("error: the model is incomplete");
    }

    [Fact]
    public async Task Check_NuGetAuditBesideARestoreWarningJson_ComposesTheNoteWithoutFlippingTheGateVerdict()
    {
        // The regression composition could introduce, pinned shut. The composed list carries the MSBuild
        // note, which is not a NuGetAudit advisory, so a gate that read that list would take the note for a
        // load failure, mark every run incomplete, and exit 2 always. The gate reads only the failed-project
        // lists; only the renderers read the composition. The restore warning is what keeps a stream, and so
        // a note, beside the advisory.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [AuditDiagnostic, PruningAdvisory], CliRunner.CleanSpecDll, false, true);

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.Strings("workspaceDiagnostics")
            .ShouldBe([PruningAdvisory, MsBuildBootstrap.SelectionNote()]);
        document.ShouldCarryPackageAudit(AuditMessage, [AuditProject]);
        document.ShouldNotBeStampedIncomplete();
        result.Err.ShouldNotContain("error: the model is incomplete");
    }

    [Fact]
    public async Task Check_NoWorkspaceDiagnostics_LeavesTheMsBuildNoteUnprinted()
    {
        // The negative control, and the reason the note is acceptable at all: it is diagnostic context, not
        // a banner. A clean run says nothing about MSBuild on any stream.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.CleanSpecDll);

        result.ShouldSucceed();
        result.Err.ShouldNotContain("MSBuild for this run");
        result.Out.ShouldNotContain("MSBuild for this run");
    }

    // ── Same-FQN merge notes render but never gate ────────────────────────────────────────────────────

    [Fact]
    public async Task Check_SameFqnAcrossTwoProjects_RendersMergeNoteWarningWithoutTrippingGate()
    {
        CliResult result = (await CollidingType.Value).Human;

        result.ShouldSucceed(); // merge notes are advisory — the gate never fires on them
        result.Err.ShouldContain(
            "warning: Projects 'MyApp.Domain' and 'MyApp.Legacy.Billing' each declare 'Shared.Widget'");
        result.Err.ShouldContain(
            "arch.Project('MyApp.Legacy.Billing') selections include it too, and each declarer's "
            + "reference to its own compiled-in copy counts against that declarer alone.");
        result.Err.ShouldNotContain("error: the model is incomplete"); // the fail-closed gate did NOT fire
    }

    [Fact]
    public async Task Check_SameFqnAcrossTwoProjectsJson_LandsMergeNoteInWorkspaceDiagnosticsWithoutTrippingGate()
    {
        CliResult result = (await CollidingType.Value).Json;

        result.ShouldSucceed(
            "\"workspaceDiagnostics\"",
            "Projects 'MyApp.Domain' and 'MyApp.Legacy.Billing' each declare 'Shared.Widget'");
        result.Out.ShouldNotContain("error: the model is incomplete");
    }

    // ── NuGetAudit advisories render but never gate ───────────────────────────────────────────────────

    [Fact]
    public async Task Check_NuGetAuditDiagnostic_NoFlag_RendersWarningWithoutTrippingGate()
    {
        // A NuGetAudit advisory is external-timing noise, not a broken model — the clean spec still exits 0,
        // and the advisory prints once, naming its project, with no MSBuild note under it.
        CliResult result = await RunWithInjectedDiagnosticAsync([AuditDiagnostic], CliRunner.CleanSpecDll, false, false);

        result.ShouldSucceed();
        result.ShouldEchoOneAuditWarning(AuditMessage, [AuditProject]);
        result.Err.ShouldNotContain("error: the model is incomplete"); // but the gate never fired
    }

    [Fact]
    public async Task Check_NuGetAuditDiagnostic_ViolatedSpecNoFlag_ExitsOneAndDoesNotGate()
    {
        // Filtering the advisory out of the gate must not mask a real violation — the violated spec exits 1.
        CliResult result = await RunWithInjectedDiagnosticAsync([AuditDiagnostic], CliRunner.ViolatedSpecDll, false, false);

        result.ShouldReportViolations();
        result.Err.ShouldNotContain("error: the model is incomplete"); // the fail-closed gate did NOT fire
    }

    [Fact]
    public async Task Check_NuGetAuditPlusLoadFailure_NoFlag_StillFailsClosed()
    {
        // An advisory riding alongside a genuine load failure: the load failure still gates, and both render.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [AuditDiagnostic, LoadDiagnostic], CliRunner.CleanSpecDll, false, false, null, [BrokenProject]);

        result.ShouldRefuseWith(
            GateLine,
            BrokenProject,
            // the advisory, grouped
            "warning: " + new PackageAuditEntry(AuditMessage, [AuditProject]).Describe(),
            $"warning: {LoadDiagnostic}", // and the load failure — no over-filtering
            "warning: MSBuild for this run:"); // with the note the load failure raises
    }

    [Fact]
    public async Task Check_NuGetAuditDiagnosticSarif_RecordsExecutionSuccessful()
    {
        // SARIF records a successful invocation: the advisory rides as one tool-execution notification but,
        // never a gate input, it does not flip executionSuccessful — the run exits 0.
        using TempDirectory temp = TestTempRoot.Fresh("gate-sarif");
        string sarifPath = temp.PathOf("gate.sarif");

        CliResult result = await RunWithInjectedDiagnosticAsync([AuditDiagnostic], CliRunner.CleanSpecDll, false, false, sarifPath);

        result.ShouldSucceed();
        string sarif = File.ReadAllText(sarifPath);
        sarif.SarifExecutionSuccessful()
            .ShouldBeTrue(sarif);
        sarif.ShouldCarryOneAuditNotification(AuditMessage, [AuditProject]);
    }

    [Fact]
    public async Task Check_OneAdvisoryOnEveryProjectAndFramework_EchoesOneWarning()
    {
        // The measured shape: one advisory against a package seven projects restore, each on four frameworks —
        // twenty-eight lines from the load, and one from the run.
        string[] projects = ["Bench", "Cli", "Core", "Core.Tests", "Json", "Json.Tests", "Testing"];
        IReadOnlyList<string> lines =
            CapturedAuditLines.PerProjectAndFramework(projects, 4, CapturedAuditLines.BuildTasksGitAdvisory);

        CliResult result = await RunWithInjectedDiagnosticAsync(lines, CliRunner.CleanSpecDll, false, false);

        result.ShouldSucceed();
        result.ShouldEchoOneAuditWarning(CapturedAuditLines.BuildTasksGitAdvisory, projects);
    }

    [Fact]
    public async Task Check_ALocalizedAuditFetchFailureJson_StaysInTheStreamWithItsNote()
    {
        // Nothing recognises a German NU1900, so nothing groups it: it stays a line of the stream, the stream
        // keeps its MSBuild note, and packageAudit never appears.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [GermanAuditFetchFailure], CliRunner.CleanSpecDll, false, true);

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.Strings("workspaceDiagnostics")
            .ShouldBe([GermanAuditFetchFailure, MsBuildBootstrap.SelectionNote()]);
        document.ShouldCarryNoPackageAudit();
    }

    [Fact]
    public async Task CheckForTheHook_OnAViolatedSpec_FlushesTheAdvisoryAsOneGroupedLine()
    {
        // A red run hands the hook its whole composed report to block with, the warnings included — so eight
        // load lines for one advisory would reach the agent as eight copies of that message. It carries one.
        string testsAdvisory = CapturedAuditLines.FramedFor("App.Tests", AuditMessage);
        IReadOnlyList<string> lines = [.. Enumerable.Repeat(AuditDiagnostic, 4), .. Enumerable.Repeat(testsAdvisory, 4)];

        CliResult result = await RunWithInjectedDiagnosticAsync(
            lines, CliRunner.ViolatedSpecDll, false, false, forTheHook: true);

        result.ShouldReportViolations();
        string grouped = "warning: " + new PackageAuditEntry(AuditMessage, ["App", "App.Tests"]).Describe();
        result.Out.Occurrences(grouped)
            .ShouldBe(1, result.Transcript());
        result.Out.ShouldNotContain(AuditDiagnostic);
    }

    [Fact]
    public async Task CheckForTheHook_OnACleanSpecWithOnlyAdvisories_WritesNothing()
    {
        // A clean run with nothing to say writes nothing, and an advisory is not something the hook says: it
        // raises no check warning, so the agent's turn ends without a word about it.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [AuditDiagnostic], CliRunner.CleanSpecDll, false, false, forTheHook: true);

        result.ShouldSucceed();
        result.Out.ShouldBeEmpty();
        result.Err.ShouldBeEmpty();
    }

    // ── no message gates, in any language ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PruningAdvisory)]
    [InlineData(GermanPruningAdvisory)]
    [InlineData(GermanAuditFetchFailure)]
    public async Task Check_RestoreWarningWithNoFailedProject_RendersItAndExitsClean(string diagnostic)
    {
        // The two measured field refusals, in three spellings. None of them says a project failed to build —
        // they are restore warnings about packages and feeds — and nothing loads them into the gate, so the
        // clean spec exits 0 and the warning still prints.
        // The German pair is the point of the theory: recognition cannot depend on a phrase, because NuGet
        // ships its wording in many languages and none of it is a contract.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [diagnostic], CliRunner.CleanSpecDll, false, false);

        result.ShouldSucceed();
        result.Err.ShouldContain($"warning: {diagnostic}");
        result.Err.ShouldNotContain("error: the model is incomplete");
    }

    [Fact]
    public async Task Check_RestoreWarningWithNoFailedProjectJson_StampsNoneOfTheWorkspaceVerdictSlots()
    {
        // The document half. A restore warning is data worth carrying, but it is not a verdict: an
        // arch_check client reading modelIncomplete must not be told the answer is untrustworthy because a
        // package could stand to be removed from a csproj. uncheckedProjects rides here too, because absent
        // is what every unfiltered run must render — a slot present-but-empty would move every golden. Nor is
        // a restore warning an advisory, so packageAudit stays absent with them.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [PruningAdvisory], CliRunner.CleanSpecDll, false, true);

        result.ShouldSucceed("will not be pruned"); // it still rides workspaceDiagnostics
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.ShouldNotBeStampedIncomplete();
        document.RootElement.TryGetProperty("failedProjects", out _)
            .ShouldBeFalse();
        document.RootElement.TryGetProperty("uncheckedProjects", out _)
            .ShouldBeFalse();
        document.RootElement.TryGetProperty("restoreFailedProjects", out _)
            .ShouldBeFalse();
        document.ShouldCarryNoPackageAudit();
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnosticJson_CarriesTheFailedProjectsBesideTheVerdict()
    {
        // What modelIncomplete cannot say on its own: which projects are missing. The diagnostics array
        // cannot be read for it — MSBuild's words about a fatal failure and about a restore warning arrive
        // in the same shape — so the evidence needs its own slot, and it is the only channel an MCP client
        // has for it.
        CliResult result = await LoadDiagnosticJson.Value;

        result.ShouldRefuseWith();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.ShouldBeStampedIncomplete();
        // Solution-relative and forward-slashed, like every other path in the document, so no machine path
        // ever lands in one a golden pins.
        document.RootElement.Strings("failedProjects")
            .ShouldHaveSingleItem()
            .ShouldEndWith("MyApp.Broken/MyApp.Broken.csproj");
    }

    // ── a narrowed universe rides the document and the SARIF, and gates neither ────────────────────────

    [Fact]
    public async Task Check_NarrowedUniverseJson_CarriesTheUncheckedProjectsWithoutMarkingTheModelIncomplete()
    {
        // The channel the human stamp can never reach: --json owns stdout, so the surface an agent reads is
        // the one with no stamp on it. What rides it must scope the verdict and not overturn it — the rules
        // all ran and all answered — so the clean spec still exits 0 and neither incomplete-model slot
        // appears beside it.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [], CliRunner.CleanSpecDll, false, true, uncheckedProjects: [UncheckedProject]);

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.ShouldNotBeStampedIncomplete();
        document.RootElement.TryGetProperty("failedProjects", out _)
            .ShouldBeFalse();
        // Solution-relative and forward-slashed, like failedProjects beside it and every other path here.
        document.RootElement.Strings("uncheckedProjects")
            .ShouldHaveSingleItem()
            .ShouldEndWith("MyApp.Skipped/MyApp.Skipped.csproj");
    }

    [Fact]
    public async Task Check_NarrowedUniverseSarif_AddsOneWarningNotificationToASuccessfulInvocation()
    {
        // Code scanning has no exit code and no stdout: a clean SARIF over a filter would close every alert
        // the unchecked projects would have raised. Warning rather than error, and executionSuccessful stays
        // true, because the results are true — they are not the whole solution's.
        using TempDirectory temp = TestTempRoot.Fresh("narrowed-sarif");
        string sarifPath = temp.PathOf("narrowed.sarif");

        CliResult result = await RunWithInjectedDiagnosticAsync(
            [], CliRunner.CleanSpecDll, false, false, sarifPath, uncheckedProjects: [UncheckedProject]);

        result.ShouldSucceed();
        string sarif = File.ReadAllText(sarifPath);
        sarif.SarifExecutionSuccessful()
            .ShouldBeTrue(sarif);
        sarif.ShouldCarryNotification(
                "warning",
                "A solution filter narrowed this run: 1 project the solution declares was not checked, so these "
                + "results cover part of the solution: ")
            .SarifMessageText()
            .ShouldContain("MyApp.Skipped/MyApp.Skipped.csproj");
    }

    // ── a failed NuGet restore gates on the same terms as a failed load ───────────────────────────────

    [Fact]
    public async Task Check_RestoreFailedProject_NoFlag_FailsClosedWithItsOwnGateLine()
    {
        // The measured silent pass. The project loaded completely — full document set, both output paths —
        // so nothing in the loaded structure blames it and a gate reading only that structure sees a clean
        // solution, while every package edge it declares is missing from the model and a rule over one reports
        // itself inert.
        CliResult result = await RestoreFailureHuman.Value;

        result.ShouldRefuseWith(RestoreGateLine, RestoreFailedProject);
    }

    [Fact]
    public async Task Check_RestoreFailedProject_NamesRestoreRatherThanBuildAsTheRemedy()
    {
        // The whole reason this is a second slot and not an extra entry in failedProjects: the repair differs.
        // A reader sent to `dotnet build` for a feed that could not be reached learns nothing.
        CliResult result = await RestoreFailureHuman.Value;

        result.Err.ShouldContain("Restore the solution (dotnet restore)");
        // The remedy leads and the warnings are offered conditionally, because this block also covers a
        // restore that never ran — which wrote no NuGet logs for the SDK to replay above it.
        result.Err.ShouldContain("any NuGet errors behind a restore that ran are in the warnings above");
        result.Err.ShouldNotContain("failed to load"); // and it does not claim the project failed to load
    }

    [Fact]
    public async Task Check_RestoreFailedProject_WithFlag_RestoresPriorExit()
    {
        // The same escape hatch, deliberately: one flag for one question — is a partial model acceptable —
        // rather than one flag per way the model can come up short.
        CliResult result = await RunWithInjectedRestoreFailureAsync(CliRunner.CleanSpecDll, true, false);

        result.ShouldSucceed();
        result.Err.ShouldNotContain("error: the model is incomplete");
    }

    [Fact]
    public async Task Check_RestoreFailedProject_ViolatedSpecNoFlag_GateTakesPrecedenceOverExitOne()
    {
        CliResult result = await RunWithInjectedRestoreFailureAsync(CliRunner.ViolatedSpecDll, false, false);

        result.ShouldRefuseWith(RestoreGateLine, RestoreFailedProject);
    }

    [Fact]
    public async Task Check_RestoreFailedProjectJson_CarriesTheRestoreFailedProjectsBesideTheVerdict()
    {
        // Its own slot, not a second entry in failedProjects: these projects loaded, so calling them failed
        // loads would be false, and the remedy a client should surface is a different command. Solution-relative
        // and forward-slashed, like every other path in the document.
        CliResult result = await RunWithInjectedRestoreFailureAsync(CliRunner.CleanSpecDll, false, true);

        result.ShouldRefuseWith();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.ShouldBeStampedIncomplete();
        document.RootElement.TryGetProperty("failedProjects", out _)
            .ShouldBeFalse();
        document.RootElement.Strings("restoreFailedProjects")
            .ShouldHaveSingleItem()
            .ShouldEndWith("MyApp.Unrestored/MyApp.Unrestored.csproj");
    }

    [Fact]
    public async Task Check_RestoreFailedProjectJsonWithTheOptOut_StillStampsTheModelIncomplete()
    {
        // The opt-out changes the exit code, never the truth about the model — which is the whole reason the
        // document stamps a fact rather than echoing a verdict.
        CliResult result = await RunWithInjectedRestoreFailureAsync(CliRunner.CleanSpecDll, true, true);

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.ShouldBeStampedIncomplete();
        document.RootElement.Strings("restoreFailedProjects")
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Check_RestoreFailedProjectSarif_RecordsExecutionUnsuccessfulAndNamesTheCause()
    {
        // Code scanning has no exit code, so the one channel that can carry a refusal there is
        // executionSuccessful, which follows the gate rather than the cause. The notification names the
        // cause: without it the cause would reach SARIF only as MSBuild's replayed prose, where a fatal
        // failure and a restore warning are the same shape.
        using TempDirectory temp = TestTempRoot.Fresh("restore-gate-sarif");
        string sarifPath = temp.PathOf("restore-gate.sarif");

        CliResult result = await RunWithInjectedRestoreFailureAsync(
            CliRunner.CleanSpecDll, false, false, sarifPath);

        result.ShouldRefuseWith();
        string sarif = File.ReadAllText(sarifPath);
        sarif.SarifExecutionSuccessful()
            .ShouldBeFalse(sarif);
        sarif.ShouldCarryNotification(
                "error",
                "The model is incomplete: NuGet packages did not resolve for 1 project, so these results were "
                + "reached against a codebase whose package references resolved to nothing: ")
            .SarifMessageText()
            .ShouldContain("MyApp.Unrestored/MyApp.Unrestored.csproj");
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnosticSarif_NamesTheFailedProjectsToo()
    {
        // The load cause gets a SARIF notification as well as the restore cause, so the channel can explain
        // either half of an incomplete model.
        using TempDirectory temp = TestTempRoot.Fresh("load-gate-sarif");
        string sarifPath = temp.PathOf("load-gate.sarif");

        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, false, sarifPath);

        result.ShouldRefuseWith();
        File.ReadAllText(sarifPath)
            .ShouldCarryNotification(
                "error",
                "The model is incomplete: 1 project failed to load, so these results were reached against a "
                + "codebase missing whole projects: ")
            .SarifMessageText()
            .ShouldContain("MyApp.Broken/MyApp.Broken.csproj");
    }

    [Fact]
    public async Task Check_LoadFailureAndRestoreFailure_WritesTheLoadBlockFirst()
    {
        // Both causes at once. A project that never loaded is more fundamentally broken than one that loaded
        // without its packages, and the two ask for different repairs, so the run is told about both — in that
        // order, and never merged into one list that would name the wrong remedy for half of it.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [], CliRunner.CleanSpecDll, false, false, null, [BrokenProject],
            restoreFailedProjects: [RestoreFailedProject]);

        result.ShouldRefuseWith(GateLine, BrokenProject, RestoreGateLine, RestoreFailedProject);
        result.Err.ShouldContainInOrder(GateLine, RestoreGateLine);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    // The load-failure shape: the diagnostic renders, and the project beside it is what gates.
    private static Task<CliResult> RunWithInjectedDiagnosticAsync(
        string spec, bool allowWorkspaceDiagnostics, bool json, string? sarif = null,
        CancellationToken? cancellationToken = null)
    {
        return RunWithInjectedDiagnosticAsync(
            [LoadDiagnostic], spec, allowWorkspaceDiagnostics, json, sarif, [BrokenProject],
            cancellationToken: cancellationToken);
    }

    // The restore-failure shape: no diagnostic at all, because the SDK's replay of NuGet's own logs is not
    // this product's to synthesize — the project beside it is the whole gate input.
    private static Task<CliResult> RunWithInjectedRestoreFailureAsync(
        string spec, bool allowWorkspaceDiagnostics, bool json, string? sarif = null,
        CancellationToken? cancellationToken = null)
    {
        return RunWithInjectedDiagnosticAsync(
            [], spec, allowWorkspaceDiagnostics, json, sarif,
            restoreFailedProjects: [RestoreFailedProject], cancellationToken: cancellationToken);
    }

    private static async Task<CliResult> RunWithInjectedDiagnosticAsync(
        IReadOnlyList<string> diagnostics, string spec, bool allowWorkspaceDiagnostics, bool json,
        string? sarif = null, IReadOnlyList<string>? failedProjects = null,
        IReadOnlyList<string>? uncheckedProjects = null,
        IReadOnlyList<string>? restoreFailedProjects = null,
        bool forTheHook = false,
        CancellationToken? cancellationToken = null)
    {
        string solution = CliRunner.MyAppSolution;
        var source = new DiagnosticInjectingSolutionSource(
            diagnostics, failedProjects, uncheckedProjects, restoreFailedProjects);
        CheckRequest request = CheckRequests.For(solution, spec, SolutionPaths.SolutionDirectoryOf(solution))
            with
            {
                Json = json, NoCache = true, AllowWorkspaceDiagnostics = allowWorkspaceDiagnostics,
                Sarif = sarif
            };

        CancellationToken ct = cancellationToken ?? Ct;
        return await CliResult.CapturedAsync((output, error) =>
        {
            var runner = new CheckRunner(output, error, source, new FakeEnvironment());
            return forTheHook ? runner.RunForHookAsync(request, ct) : runner.RunAsync(request, ct);
        });
    }

    // Declares Shared.Widget in two projects that do not reference each other (Domain → Web → Billing, so
    // Domain and Billing share no edge), producing a real same-FQN cross-project conflation with no compile
    // conflict — nothing uses Widget, so no use site is ambiguous. Ordinal project order makes Domain the
    // first declarer (winner) and Legacy.Billing the loser.
    private static void WriteCollidingType(TempFixtureWorkspace workspace)
    {
        const string widget = "namespace Shared;\npublic class Widget { }\n";
        File.WriteAllText(workspace.PathOf("MyApp.Domain", "Widget.cs"), widget);
        File.WriteAllText(workspace.PathOf("MyApp.Legacy.Billing", "Widget.cs"), widget);
    }
}
