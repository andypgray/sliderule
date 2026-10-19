using System.Text.Json;
using Shouldly;
using SlideRule.Cli.Mcp;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Mcp.Pipeline;
using SlideRule.Cli.Mcp.Tools;
using SlideRule.Cli.Pipeline;
using SlideRule.Roslyn;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     The MSBuild-selection note on the MCP surface, the incomplete-model verdict beside it, which is the
///     whole refusal a surface with no exit code has, and the NuGet audit advisories reported apart from both.
/// </summary>
/// <remarks>
///     <para>
///         <c>ArchTools</c> hands every runner <see cref="TextWriter.Null" /> as the error writer on the
///         claim that everything the error writer would carry is in the document. The note is composed into
///         the list both surfaces read rather than appended to stderr at write time, and this is where that
///         claim is held — a client that sees a load failure also sees which MSBuild produced it, which is
///         nearly always the next question.
///     </para>
///     <para>
///         The advisories have a key of their own for this surface's sake. An <c>arch_check</c> reply is fitted
///         to the client's response budget, and a load that repeats an advisory once per project per framework
///         spent that budget on the repeats, handing back a coarser report than the call asked for. The last
///         rows pin the grain such a load keeps, against the budget the server itself composes.
///     </para>
///     <para>
///         The tool is constructed directly rather than driven through <see cref="McpPipelineHarness" />,
///         because the subject is what the tool passes to its runner: the harness composes its own
///         <c>ISolutionSource</c> from DI, and the injected diagnostics are the only way to reach the note
///         over a fixture that loads perfectly well.
///     </para>
/// </remarks>
public sealed class ArchToolsWorkspaceDiagnosticsTests
{
    private const string LoadDiagnostic = DiagnosticInjectingSolutionSource.SimulatedLoadFailure;

    private const string UnrestoredProject = "C:/repo/MyApp.Unrestored/MyApp.Unrestored.csproj";

    // NuGet's advice to prune a package reference: an ordinary restore warning, so a line of the stream rather
    // than an advisory.
    private const string RestoreWarning =
        CapturedAuditLines.FramePrefix + "/src/App/App.csproj" + CapturedAuditLines.FrameSeparator
        + "PackageReference System.Text.Encodings.Web will not be pruned. Consider removing this package "
        + "from your dependencies, as it is likely unnecessary.";

    // How many frameworks each advisory-raising project restores on, and so how many times the load repeats
    // that project's line.
    private const int FrameworksPerProject = 4;

    private static readonly string AppAdvisory =
        CapturedAuditLines.FramedFor("App", CapturedAuditLines.BuildTasksGitAdvisory);

    private static readonly string TestsAdvisory =
        CapturedAuditLines.FramedFor("App.Tests", CapturedAuditLines.BuildTasksGitAdvisory);

    // The one check two rows read, an advisory beside a load failure: read-only, so a shared result leaks nothing.
    private static readonly Lazy<Task<string>> AdvisoryBesideLoadFailure =
        new(() => ToolsWith([AppAdvisory, LoadDiagnostic]).CheckAsync(cancellationToken: Ct));

    [Fact]
    public async Task ArchCheck_WorkspaceLoadDiagnostic_CarriesTheMsBuildNoteLastInTheDocument()
    {
        ArchTools tools = ToolsWith([LoadDiagnostic]);

        string document = await tools.CheckAsync(cancellationToken: Ct);

        CheckJson.Strings(document, "workspaceDiagnostics")
            .ShouldBe([LoadDiagnostic, MsBuildBootstrap.SelectionNote()]);
    }

    [Fact]
    public async Task ArchCheck_NoWorkspaceDiagnostics_CarriesNoNote()
    {
        // The negative control that keeps the note diagnostic context rather than a banner: an empty
        // composition stays empty, so a clean call's document says nothing about MSBuild at all.
        ArchTools tools = ToolsWith([]);

        string document = await tools.CheckAsync(cancellationToken: Ct);

        CheckJson.Strings(document, "workspaceDiagnostics")
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task ArchCheck_RestoreFailedProject_StampsTheModelIncompleteVerdict()
    {
        // The MCP surface has no exit code, so modelIncomplete is the whole refusal a client can read. It
        // follows the gate rather than either cause, so a further cause needs no change to any tool.
        ArchTools tools = ToolsWith([], restoreFailedProjects: [UnrestoredProject]);

        string document = await tools.CheckAsync(cancellationToken: Ct);

        using JsonDocument parsed = JsonDocument.Parse(document);
        parsed.RootElement.GetProperty("modelIncomplete")
            .GetBoolean()
            .ShouldBeTrue();
    }

    [Fact]
    public async Task ArchGraph_RestoreFailedProject_RefusesWithTheRestoreWordingAndBothDialectsOfTheOptOut()
    {
        // graph carries its evidence inline because there is nothing above it here — this tool discards its
        // error writer — and it is the entry point a stranger reaches for first, so the refusal names the
        // remedy and the opt-out in both dialects rather than assuming which surface asked.
        ArchTools tools = ToolsWith([], restoreFailedProjects: [UnrestoredProject]);

        var error = await Should.ThrowAsync<UserErrorException>(() => tools.GraphAsync(cancellationToken: Ct));

        error.Message.ShouldContain(
            "the model is incomplete — NuGet packages did not resolve for 1 project, so graph cannot survey "
            + "the codebase: the external references a survey exists to show are exactly what did not "
            + "resolve:");
        error.Message.ShouldContain(UnrestoredProject);
        error.Message.ShouldContain("Restore the solution first (dotnet restore)");
        error.Message.ShouldContain("allowWorkspaceDiagnostics: true (arch_graph)");
    }

    [Fact]
    public async Task ArchGraph_RestoreFailedProjectWithTheOptOut_SurveysThePartialModel()
    {
        // The opt-out the refusal names, taken: one flag for one question, whichever way the model came up
        // short.
        ArchTools tools = ToolsWith([], restoreFailedProjects: [UnrestoredProject]);

        string document = await tools.GraphAsync(true, cancellationToken: Ct);

        using JsonDocument parsed = JsonDocument.Parse(document);
        parsed.RootElement.GetProperty("modelIncomplete")
            .GetBoolean()
            .ShouldBeTrue();
    }

    [Fact]
    public async Task ArchCheck_AdvisoriesAlone_CarryOneEntryBesideAnEmptyStreamWithNoNote()
    {
        // The note answers which MSBuild opened a project, and an advisory raises no such question, so a call
        // whose only diagnostics are advisories gets the entry and a stream with nothing in it.
        string document = await ToolsWith([AppAdvisory, AppAdvisory, TestsAdvisory])
            .CheckAsync(cancellationToken: Ct);

        document.ShouldCarryPackageAudit(CapturedAuditLines.BuildTasksGitAdvisory, ["App", "App.Tests"]);
        CheckJson.Strings(document, "workspaceDiagnostics")
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task ArchCheck_AnAdvisoryBesideALoadFailure_KeepsTheNoteWithTheLoadFailure()
    {
        string document = await AdvisoryBesideLoadFailure.Value;

        document.ShouldCarryPackageAudit(CapturedAuditLines.BuildTasksGitAdvisory, ["App"]);
        CheckJson.Strings(document, "workspaceDiagnostics")
            .ShouldBe([LoadDiagnostic, MsBuildBootstrap.SelectionNote()]);
    }

    [Fact]
    public async Task ArchCheck_IndexGrain_ElidesTheAdvisoriesToTheirCount()
    {
        // Each array elides to its own count, and a count of nothing is never written: this stream is empty,
        // so the advisories' count stands alone.
        string document = await ToolsWith([AppAdvisory])
            .CheckAsync(grain: "index", cancellationToken: Ct);

        document.ShouldElidePackageAuditTo(1);
        document.ShouldNotContain("\"workspaceDiagnosticCount\"");
    }

    [Fact]
    public async Task ArchCheck_PackageAudit_SerializesBetweenTheRulesAndTheStream()
    {
        // Below the verdict with the rest of the load's words, and above the stream, whose bulk has no ceiling.
        string document = await AdvisoryBesideLoadFailure.Value;

        document.ShouldSerializeBefore("rules", "packageAudit");
        document.ShouldSerializeBefore("packageAudit", "workspaceDiagnostics");
    }

    [Fact]
    public async Task ArchGraph_AdvisoriesAlone_CarryOneEntryAndNoStreamKey()
    {
        // The survey omits an empty stream rather than writing a bare array, so an advisory-only load leaves
        // no stream key at all, only the entry.
        string document = await ToolsWith([AppAdvisory, TestsAdvisory])
            .GraphAsync(cancellationToken: Ct);

        document.ShouldCarryPackageAudit(CapturedAuditLines.BuildTasksGitAdvisory, ["App", "App.Tests"]);
        document.ShouldNotContain("\"workspaceDiagnostic");
    }

    [Fact]
    public async Task ArchGraph_IndexGrain_ElidesTheAdvisoriesToTheirCount()
    {
        string document = await ToolsWith([AppAdvisory, TestsAdvisory])
            .GraphAsync(grain: "index", cancellationToken: Ct);

        document.ShouldElidePackageAuditTo(1);
    }

    [Fact]
    public async Task ArchStatus_AdvisoriesAlone_CarryOneEntryAndNoStreamKey()
    {
        // The burndown document carries the same entries check does, so arch_status is not the one surface
        // where an advisory still arrives as the load's raw lines.
        string document = await ToolsWith([AppAdvisory])
            .StatusAsync(Ct);

        document.ShouldCarryPackageAudit(CapturedAuditLines.BuildTasksGitAdvisory, ["App"]);
        document.ShouldNotContain("\"workspaceDiagnostics\"");
    }

    [Fact]
    public async Task ArchStatus_AnAdvisoryBesideARestoreWarning_ReportsEachUnderItsOwnKey()
    {
        string document = await ToolsWith([AppAdvisory, RestoreWarning])
            .StatusAsync(Ct);

        document.ShouldCarryPackageAudit(CapturedAuditLines.BuildTasksGitAdvisory, ["App"]);
        CheckJson.Strings(document, "workspaceDiagnostics")
            .ShouldBe([RestoreWarning, MsBuildBootstrap.SelectionNote()]);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("overview", null)]
    [InlineData(null, "data-access/*")]
    public async Task ArchCheck_AdvisoriesThatWouldOverrunTheDefaultBudget_KeepTheRequestedGrain(
        string? grain, string? rules)
    {
        // The measured failure, sized against the budget the server composes rather than a literal, so the pin
        // follows the default wherever it moves. Enough projects repeat one advisory on four frameworks each
        // that their raw lines alone would carry the report past that budget; grouped, the report keeps the
        // grain the call asked for, sites and all.
        var budget = new ResponseBudget(new FakeEnvironment());
        int maxChars = budget.MaxChars();
        McpServerBinding binding = McpServerBindings.MyAppWithViolatedSpec();

        int lineLength = BedProjectAdvisory(0).Length;
        int projectCount = maxChars / (lineLength * FrameworksPerProject) + 1;
        string[] projects = [.. Enumerable.Range(0, projectCount).Select(BedProjectName)];
        IReadOnlyList<string> lines = CapturedAuditLines.PerProjectAndFramework(
            projects, FrameworksPerProject, CapturedAuditLines.BuildTasksGitAdvisory);
        lines.Sum(line => line.Length)
            .ShouldBeGreaterThan(maxChars, "the raw lines must overrun the budget on their own, or this pins nothing");

        var tools = new ArchTools(binding, new DiagnosticInjectingSolutionSource(lines), new SystemEnvironment(), new BudgetedResponseFitter(budget));
        string document = await tools.CheckAsync(rules: rules, grain: grain, cancellationToken: Ct);

        using JsonDocument parsed = JsonDocument.Parse(document);
        string? stampedGrain = parsed.RootElement.StringOrDefault("grain");
        stampedGrain.ShouldBe(grain, document);
        parsed.ShouldCarryPackageAudit(CapturedAuditLines.BuildTasksGitAdvisory, projects);
        parsed.RootElement.Strings("workspaceDiagnostics")
            .ShouldBeEmpty();
    }

    // The tools over the clean spec, with diagnostics and any restore-failed projects injected into an otherwise
    // healthy load.
    private static ArchTools ToolsWith(
        IReadOnlyList<string> diagnostics, IReadOnlyList<string>? restoreFailedProjects = null)
    {
        return new ArchTools(
            McpServerBindings.MyAppWithCleanSpec(),
            new DiagnosticInjectingSolutionSource(diagnostics, restoreFailedProjects: restoreFailedProjects),
            new SystemEnvironment(),
            ResponseFitter.FirstRung);
    }

    // Fixed width, so every project's line is the same length and the count that overruns the budget is exact.
    private static string BedProjectName(int index)
    {
        return $"Bed.Project{index:000}";
    }

    private static string BedProjectAdvisory(int index)
    {
        return CapturedAuditLines.FramedFor(BedProjectName(index), CapturedAuditLines.BuildTasksGitAdvisory);
    }
}
