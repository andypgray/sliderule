using Shouldly;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>The workspace-diagnostics contract on <c>baseline</c>, run after a failing <c>check</c>.</summary>
/// <remarks>
///     <para>
///         The sequence a team actually runs is <c>check</c> then <c>baseline init</c>, and both fail closed
///         on a partial model. Rendering the warnings, carrying on, writing a baseline from that model and
///         exiting 0 is the worst answer <c>baseline</c> could give, because a baseline is the team's
///         signature on its debt: unloaded projects declare no types, so <c>init</c> would capture "zero
///         debt" for rules that were never measured, and every later ratchet reading would be taken against
///         that signature.
///     </para>
///     <para>
///         Driven through <see cref="BaselineRunner" /> with a
///         <see cref="DiagnosticInjectingSolutionSource" /> over a private copy of the MyApp fixture — the
///         fixture loads cleanly by design, so injection is the only way to put a load failure in front of
///         a command that then writes files. The genuinely-partial load lives in
///         <see cref="PartialLoadWorkspaceE2ETests" />; what this class owns is the write.
///     </para>
/// </remarks>
public sealed class BaselineWorkspaceDiagnosticsGateE2ETests
{
    private const string LoadDiagnostic = DiagnosticInjectingSolutionSource.SimulatedLoadFailure;

    // A NuGetAudit advisory in the codeless shape Roslyn actually delivers one: external publication timing,
    // not a broken model.
    private const string AuditMessage = CapturedAuditLines.CryptographyXmlAdvisory;

    private const string AuditProject = "App";

    private static readonly string AuditDiagnostic = CapturedAuditLines.FramedFor(AuditProject, AuditMessage);

    // The advisory as baseline echoes it, the way every verb does: NuGet's message once, naming the project
    // the frame named. Asserted present, because nothing else pins that baseline still echoes advisories.
    private static readonly string AuditWarning =
        "warning: " + new PackageAuditEntry(AuditMessage, [AuditProject]).Describe();

    // The project the gate is told failed to load. Injected separately from the diagnostic above, because
    // the product separates them: the diagnostic renders and the project decides.
    private const string BrokenProject = DiagnosticInjectingSolutionSource.SimulatedFailedProject;

    private const string GateLine =
        "error: the model is incomplete — 1 project failed to load, so no baseline was written: a baseline "
        + "captured from a partial model signs off debt that was never measured:";

    private const string CheckGateLine =
        "error: the model is incomplete — 1 project failed to load, so check cannot pass:";

    // The project the gate is told restored badly. It loaded completely, so only its assets file says so.
    private const string UnrestoredProject = DiagnosticInjectingSolutionSource.SimulatedRestoreFailedProject;

    private const string RestoreGateLine =
        "error: the model is incomplete — NuGet packages did not resolve for 1 project, so no baseline was "
        + "written: every violation resting on a package edge is absent from this model, so the baseline "
        + "would sign off debt it could not see:";

    private static readonly string[] ConventionalBaselineFile = ["arch", "baselines", "data-access", "no-inline-sql.json"];

    [Fact]
    public async Task CheckThenBaselineInit_WorkspaceLoadDiagnostic_BothFailClosedAndNothingIsWritten()
    {
        // The sequence a team runs, in order: check, then baseline init, and both must refuse.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile); // uncaptured, so a run that reached the write would create it

        CliResult check = await RunCheckAsync(workspace, [LoadDiagnostic], false, [BrokenProject]);
        check.ShouldRefuseWith(CheckGateLine, BrokenProject);

        CliResult init = await RunBaselineAsync(
            workspace, [LoadDiagnostic], BaselineRequests.Init, false, [BrokenProject]);

        init.ShouldRefuseWith(
            GateLine,
            BrokenProject,
            $"warning: {LoadDiagnostic}"); // the load failure still prints as a warning
        init.Out.ShouldBeEmpty(); // it refused before the ratchet survey, so it reported no per-rule outcome
        File.Exists(baselineFile)
            .ShouldBeFalse(); // and above all, wrote nothing
    }

    [Fact]
    public async Task BaselineInit_WorkspaceLoadDiagnosticWithFlag_WritesTheBaselineAndStillWarns()
    {
        // The escape hatch, and the negative control for "wrote nothing" above: the operator opts into the
        // partial model, the warnings still print, and the capture happens.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile);

        CliResult init = await RunBaselineAsync(workspace, [LoadDiagnostic], BaselineRequests.Init, true);

        init.ShouldSucceed("wrote");
        init.Err.ShouldContain($"warning: {LoadDiagnostic}");
        init.Err.ShouldNotContain("error: the model is incomplete");
        File.Exists(baselineFile)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task BaselineAcceptReductions_WorkspaceLoadDiagnostic_FailsClosedBeforeShrinkingAnything()
    {
        // The dangerous mode. accept-reductions removes entries whose violation "no longer occurs" — and a
        // project that stopped loading looks exactly like a violation that stopped, so an ungated run would
        // shrink the ratchet on the strength of nothing at all. The checked-in baseline must survive byte
        // for byte.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        byte[] before = File.ReadAllBytes(baselineFile);

        CliResult accept = await RunBaselineAsync(
            workspace, [LoadDiagnostic], BaselineRequests.AcceptReductions, false, [BrokenProject]);

        accept.ShouldRefuseWith(GateLine, BrokenProject);
        File.ReadAllBytes(baselineFile)
            .ShouldBe(before);
    }

    [Fact]
    public async Task BaselineAdd_WorkspaceLoadDiagnostic_FailsClosedBeforeGrandfatheringAnything()
    {
        // The third mode that extracts the codebase gates too. add resolves the entry it grandfathers
        // against the current violations, so a partial model can make it refuse a real one or attribute the
        // wrong one; either way the ratchet grows against a model nobody checked.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        byte[] before = File.ReadAllBytes(baselineFile);

        CliResult add = await RunBaselineAsync(workspace, [LoadDiagnostic], AddRequest, false, [BrokenProject]);

        add.ShouldRefuseWith(GateLine, BrokenProject);
        File.ReadAllBytes(baselineFile)
            .ShouldBe(before);
    }

    [Fact]
    public async Task BaselineInit_NuGetAuditDiagnostic_WritesTheBaselineWithoutTrippingTheGate()
    {
        // Carve-out parity with check: an advisory is external-timing noise, not a broken model, so it
        // renders and does not gate. Getting this wrong on baseline is how the gate bypass would return on a
        // different verb.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile);

        CliResult init = await RunBaselineAsync(workspace, [AuditDiagnostic], BaselineRequests.Init, false);

        init.ShouldSucceed();
        init.Err.ShouldContain(AuditWarning); // the advisory still renders
        init.Err.ShouldNotContain("error: the model is incomplete");
        File.Exists(baselineFile)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task BaselineInit_NuGetAuditPlusLoadFailure_StillFailsClosed()
    {
        // The carve-out must not over-filter: an advisory riding alongside a genuine load failure still gates.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile);

        CliResult init = await RunBaselineAsync(
            workspace, [AuditDiagnostic, LoadDiagnostic], BaselineRequests.Init, false, [BrokenProject]);

        init.ShouldRefuseWith(GateLine, BrokenProject, AuditWarning, $"warning: {LoadDiagnostic}");
        File.Exists(baselineFile)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task BaselineInit_RestoreFailedProject_FailsClosedAndWritesNothing()
    {
        // The same signature, the other way to be missing what you are signing for. A baseline captured while
        // a project's packages resolved to nothing records every violation resting on a package edge as
        // absent — and the team has then signed off debt no run ever measured.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile);

        CliResult init = await RunBaselineAsync(
            workspace, [], BaselineRequests.Init, false, restoreFailedProjects: [UnrestoredProject]);

        init.ShouldRefuseWith(RestoreGateLine, UnrestoredProject);
        init.Out.ShouldBeEmpty();
        File.Exists(baselineFile)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task BaselineAcceptReductions_RestoreFailedProject_FailsClosedBeforeShrinkingAnything()
    {
        // The dangerous mode, on the cause that reaches it most quietly: a package that stopped resolving
        // looks exactly like a violation that stopped occurring, and accept-reductions would delete the
        // real entry on the strength of it.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        byte[] before = File.ReadAllBytes(baselineFile);

        CliResult accept = await RunBaselineAsync(
            workspace, [], BaselineRequests.AcceptReductions, false, restoreFailedProjects: [UnrestoredProject]);

        accept.ShouldRefuseWith(RestoreGateLine, UnrestoredProject);
        File.ReadAllBytes(baselineFile)
            .ShouldBe(before);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    // The add companions name a violation the ViolatedSpec really does report, so a run that got past the
    // gate would succeed rather than refuse for an unrelated reason.
    private static BaselineRequest AddRequest(string workingDirectory)
    {
        return BaselineRequests.Add(
            "data-access/no-inline-sql", "INC-1234", "MyApp.Web.HomeController", "System.Data.DataTable",
            workingDirectory);
    }

    private static Task<CliResult> RunBaselineAsync(
        TempFixtureWorkspace workspace,
        IReadOnlyList<string> diagnostics,
        Func<string, BaselineRequest> mode,
        bool allowWorkspaceDiagnostics,
        IReadOnlyList<string>? failedProjects = null,
        IReadOnlyList<string>? restoreFailedProjects = null)
    {
        var source = new DiagnosticInjectingSolutionSource(
            diagnostics, failedProjects, restoreFailedProjects: restoreFailedProjects);

        // Cache-free: the subject is what an injected load failure does to the gate, and a cache hit would
        // replay a real run's diagnostics over the injected ones.
        BaselineRequest request = mode(SolutionPaths.SolutionDirectoryOf(workspace.SolutionPath))
            with
            {
                Solution = workspace.SolutionPath,
                Spec = CliRunner.ViolatedSpecDll,
                NoCache = true,
                AllowWorkspaceDiagnostics = allowWorkspaceDiagnostics
            };

        return CliResult.CapturedAsync((output, error) => new BaselineRunner(output, error, source, new SystemEnvironment())
            .RunAsync(request, Ct));
    }

    private static Task<CliResult> RunCheckAsync(
        TempFixtureWorkspace workspace, IReadOnlyList<string> diagnostics, bool allowWorkspaceDiagnostics,
        IReadOnlyList<string>? failedProjects = null)
    {
        var source = new DiagnosticInjectingSolutionSource(diagnostics, failedProjects);
        CheckRequest request = CheckRequests.For(
                workspace.SolutionPath, CliRunner.ViolatedSpecDll,
                SolutionPaths.SolutionDirectoryOf(workspace.SolutionPath))
            with
            {
                NoCache = true, AllowWorkspaceDiagnostics = allowWorkspaceDiagnostics
            };

        return CliResult.CapturedAsync((output, error) => new CheckRunner(output, error, source, new FakeEnvironment()).RunAsync(request, Ct));
    }
}
