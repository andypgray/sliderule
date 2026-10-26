using Shouldly;
using SlideRule.Cli;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>The two ways <c>baseline rename</c> reaches a model, and what neither of them does.</summary>
/// <remarks>
///     <para>
///         A convention or csproj <c>--spec</c> opens a workspace for resolution only: the load's diagnostics are
///         rendered, a failed project does not refuse the run as it refuses <c>init</c>, and nothing is extracted.
///         A built-DLL <c>--spec</c> takes the fast path and opens no workspace at all. Both rows end in one of
///         the file-work refusals, so the run exits 2 on the rename's own sentence and writes nothing, which is
///         what lets them run over this repository's solution and the committed MyApp fixture.
///     </para>
///     <para>
///         The workspace row runs over this repository's own solution and arch-spec csproj, as
///         <see cref="ExplainWorkspaceDiagnosticsE2ETests" /> does, because MyApp declares no spec project. Each
///         row runs the runner under the CLI's own error handler, so the refusal reaches stderr and the exit code
///         the command line would print, and points the cache at a private root.
///     </para>
/// </remarks>
public sealed class BaselineRenameWorkspaceE2ETests
{
    private const string LoadDiagnostic = DiagnosticInjectingSolutionSource.SimulatedLoadFailure;
    private const string FailedProject = DiagnosticInjectingSolutionSource.SimulatedFailedProject;

    [Fact]
    public async Task Rename_WorkspacePathWithAFailedLoad_RendersTheDiagnosticsNeverGatesAndNeverExtracts()
    {
        using var cache = new TempCacheRoot("baseline-rename");
        var source = new DiagnosticInjectingSolutionSource([LoadDiagnostic], [FailedProject]);
        FakeEnvironment environment = FakeEnvironment.WithCacheRoot(cache.Root);
        BaselineRequest request = BaselineRequests.Rename("x/old", "cli/no-stdout", RepoRoot.Directory)
            with
            {
                Solution = RepoRoot.Solution,
                Spec = RepoRoot.ArchitectureSpecCsproj
            };
        BaselineRunner? runner = null;

        CliResult result = await CliResult.CapturedAsync((output, error) =>
        {
            var created = new BaselineRunner(output, error, source, environment);
            runner = created;
            return CommandEntryPoint.RunAsync(() => created.RunAsync(request, Ct), error, Ct);
        });

        result.ShouldRefuseWith(
            $"warning: {LoadDiagnostic}",
            "rule 'cli/no-stdout' is not ratcheted — only Migrate and Quarantine containment rules carry baselines.");
        result.Err.ShouldNotContain("error: the model is incomplete", result.Transcript());
        result.Out.ShouldBeEmpty(result.Transcript());
        runner.ShouldNotBeNull()
            .LastExtractionCount.ShouldBe(0);
    }

    [Fact]
    public async Task Rename_BuiltDllSpec_OpensNoWorkspace()
    {
        using var cache = new TempCacheRoot("baseline-rename");
        var counting = new CountingSolutionSource();
        FakeEnvironment environment = FakeEnvironment.WithCacheRoot(cache.Root);
        string solutionDirectory = Path.GetDirectoryName(CliRunner.MyAppSolution)!;
        BaselineRequest request = BaselineRequests.Rename("x/old", "data-access/no-inline-sql", solutionDirectory)
            with
            {
                Solution = CliRunner.MyAppSolution,
                Spec = CliRunner.ViolatedSpecDll
            };

        CliResult result = await CliResult.CapturedAsync((output, error) =>
            CommandEntryPoint.RunAsync(
                () => new BaselineRunner(output, error, counting, environment).RunAsync(request, Ct), error, Ct));

        // The committed conventional file already holds the rule's section, so the rename refuses there.
        result.ShouldRefuseWith(
            "rule 'data-access/no-inline-sql' already has a baseline section in "
            + "arch/baselines/data-access/no-inline-sql.json; nothing was renamed.");
        counting.AcquireCount.ShouldBe(0);
    }
}
