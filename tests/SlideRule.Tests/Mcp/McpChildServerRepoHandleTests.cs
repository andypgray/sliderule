using System.Diagnostics;
using System.Reflection;
using Shouldly;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     The property no architecture rule can express: a long-lived host must not retain a handle on this
///     repository's build outputs. Its two known failure shapes — a diff child inheriting the server's
///     JSON-RPC stdin, and path-loaded spec assemblies pinning the build output for the server's whole
///     lifetime — are checked here against a real child process over real stdio.
/// </summary>
/// <remarks>
///     <para>
///         The tests run a live child because retention is a property of a process, not of a call: the
///         in-process harnesses load the same code into the test host, whose own footprint under the
///         repository is enormous and irrelevant. Only a separate process has a footprint that can be read
///         and required to be empty. <see cref="ProcessFileFootprint" /> reads it with two scans, mapped
///         views and handles.
///     </para>
///     <para>
///         A detector that silently sees nothing reports every process as clean.
///         <see cref="InRepoServer_MapsAndHoldsPathsUnderTheRepository" /> is therefore a committed negative
///         control: the same server launched the in-repo way (its images mapped out of the test output, its
///         working directory inside the repository) must be caught by both scans. It proves that
///         the zero its sibling asserts means something, and it reds if either scan or the device-path
///         translation behind it regresses.
///     </para>
///     <para>
///         Both tests count only what the server acquired for itself: they exclude the handles this process
///         already held when it started the child (see <see cref="LauncherHandlesUnderRepo" />). The
///         exclusion is exact-path and handle-only, and the negative control proves it does not gut the
///         scan, because the in-repo server's own working-directory handle survives it.
///     </para>
///     <para>
///         A profiler is the same story on the mapped-view side, which
///         inheritance cannot reach: under a coverage run the child inherits the environment naming the CLR
///         profiler and maps the collector's engine out of the test output, under this repository
///         (measured), and nothing the server does can decline it. So both tests discount whatever sits
///         under <see cref="ProcessFileFootprint.InjectedProfilerDirectory" /> — null, excusing nothing,
///         with no profiler attached. A profiler the harness forced in is not the server pinning the tree.
///     </para>
/// </remarks>
public sealed class McpChildServerRepoHandleTests
{
    /// <summary>Every read tool, in call order, each with the arguments it is called with.</summary>
    private static readonly (string Tool, string Arguments)[] ReadToolCalls =
    [
        ("arch_check", """{"diffBase":"HEAD"}"""),
        ("arch_status", "{}"),
        ("arch_graph", "{}"),
        ("arch_explain", """{"ruleId":"legacy/billing/containment"}"""),
        ("arch_context", """{"path":"MyApp.Legacy.Billing"}""")
    ];

    [Fact]
    public async Task StagedServer_AfterEveryReadTool_HoldsNothingUnderTheRepository()
    {
        ProcessFileFootprint.SkipUnlessSupported();

        string stagedServer = Path.Combine(TestTempRoot.For("staged-server"), "cli");
        McpChildHarness.StageDirectory(Path.GetDirectoryName(SourceTreeCliDll())!, stagedServer);

        using var repo = new TempGitRepo();
        // The same untracked file in dragon territory the stdio suite uses, so arch_check's report can only
        // carry the tripwire if git actually ran — proof the whole diff path executed, not just the handshake.
        repo.WriteQuarantineNote();

        // Deployment shape: the server staged outside the repository and its working directory outside too —
        // but the spec left at its build-output path *inside* the repository, because that is the half the
        // byte-loading fix protects. A path load here would pin the spec DLL and the assemblies beside it for
        // the server's whole lifetime, and every build of that project would fail until it was killed.
        ProcessStartInfo startInfo = McpChildHarness.ServerStartInfo(
            Path.Combine(stagedServer, "sliderule-cli.dll"),
            repo.SolutionPath,
            CliRunner.QuarantinedSpecDll,
            repo.Root);

        IReadOnlyList<RetainedPath> inherited = LauncherHandlesUnderRepo();
        string? injected = ProcessFileFootprint.InjectedProfilerDirectory();

        // The footprint assertion is the only one that has to run against a live process, so it rides the
        // harness's whileComplete hook — which fires only on a conversation that actually completed, so a
        // wedge is reported as a wedge below rather than as a footprint that never formed.
        ChildConversation conversation = await McpChildHarness.ConverseAsync(
            startInfo,
            ReadToolCalls,
            whileComplete: server =>
                server.ShouldEventuallyHoldNoPathsUnder(RepoRoot.Directory, inherited, injected));

        conversation.Handshake.ShouldNotBeNull(
            $"the staged MCP server never answered `initialize`.\nstderr:\n{conversation.Diagnostics}");

        foreach (string name in ReadToolCalls.Select(call => call.Tool))
        {
            string? answer = conversation.Answers.GetValueOrDefault(name);
            answer.ShouldNotBeNull($"the {name} response never arrived.\nstderr:\n{conversation.Diagnostics}")
                .ShouldHaveToolText(name)
                .ShouldNotBeNullOrEmpty($"{name} answered with an empty payload.");
        }

        conversation.StillAlive.ShouldBeTrue(
            $"the server exited during the run, so its footprint proves nothing.\nstderr:\n{conversation.Diagnostics}");

        // arch_check must have run the whole diff path, spec load included — otherwise a server
        // that answered five errors would hold nothing and pass.
        conversation.Answers["arch_check"]!
            .ShouldHaveToolText("arch_check")
            .ShouldContain("quarantinedScopeTouched");
    }

    [Fact]
    public async Task InRepoServer_MapsAndHoldsPathsUnderTheRepository()
    {
        ProcessFileFootprint.SkipUnlessSupported();

        // The in-repo launch, deliberately: the server dll beside the test assembly, and a working
        // directory inside the repository. No tool call — a child's own images are mapped at startup, which
        // is all this control needs, and the handshake is the cheapest proof it got that far.
        ProcessStartInfo startInfo = McpChildHarness.ServerStartInfo(
            TestsBinCli.Dll(),
            CliRunner.MyAppSolution,
            CliRunner.QuarantinedSpecDll,
            AppContext.BaseDirectory);

        IReadOnlyList<RetainedPath> inherited = LauncherHandlesUnderRepo();
        string? injected = ProcessFileFootprint.InjectedProfilerDirectory();

        IReadOnlyList<RetainedPath> retained = [];

        // No calls, so whileComplete fires on the handshake alone — and the scan is the only thing it has to
        // do here, because a footprint can only be read from a process that is still up.
        ChildConversation conversation = await McpChildHarness.ConverseAsync(
            startInfo,
            [],
            whileComplete: server => retained = server.AcquiredPathsUnder(RepoRoot.Directory, inherited, injected));

        conversation.Handshake.ShouldNotBeNull(
            $"the MCP server never answered `initialize` over real stdio.\nstderr:\n{conversation.Diagnostics}");

        retained.ShouldContain(
            path => path.Scan == FootprintScan.MappedView,
            "the mapped-view scan found no image under the repository, though this server was launched from "
            + $"the build output inside it. The scan is blind, and its sibling's zero means nothing.\n{Format(retained)}");

        retained.ShouldContain(
            path => path.Scan == FootprintScan.Handle,
            "the handle scan found nothing this server opened for itself under the repository, though its "
            + "working directory is inside it and a process holds a handle on its own working directory. "
            + $"Either the scan is blind or the inherited-handle exclusion is swallowing real hits.\n{Format(retained)}");
    }

    /// <summary>
    ///     What this test process already holds under the repository, taken before any child exists.
    /// </summary>
    /// <remarks>
    ///     A child started with redirected streams is created with handle inheritance on, so it is handed a
    ///     copy of every inheritable handle this process holds — the current-directory handle among them,
    ///     which is inside the repository whenever the suite is run from it. Those are not paths the server
    ///     opened and no server-side change can release them, so they are excluded from both tests.
    ///     Nothing is excused on the mapped-view side, where a view cannot be inherited at all.
    /// </remarks>
    private static IReadOnlyList<RetainedPath> LauncherHandlesUnderRepo()
    {
        using var self = Process.GetCurrentProcess();
        return self.HandlePathsUnder(RepoRoot.Directory);
    }

    /// <summary>
    ///     The CLI project's own build output, not the copy beside the test assembly: this is what the
    ///     launcher stages, and staging it is the point — a server run from the build tree maps its images
    ///     out of the repository and could never hold nothing.
    /// </summary>
    private static string SourceTreeCliDll()
    {
        string configuration = typeof(McpChildServerRepoHandleTests).Assembly
                                   .GetCustomAttribute<AssemblyConfigurationAttribute>()
                                   ?.Configuration
                               ?? throw new InvalidOperationException(
                                   "The tests assembly carries no AssemblyConfigurationAttribute, so the "
                                   + "matching CLI build output cannot be located.");

        string path = Path.Combine(
            RepoRoot.Directory, "src", "SlideRule.Cli", "bin", configuration, "net10.0", "sliderule-cli.dll");

        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"The CLI's own build output was not found at '{path}'. Build src/SlideRule.Cli in the "
                + $"'{configuration}' configuration and re-run.");

        return path;
    }

    private static string Format(IReadOnlyList<RetainedPath> retained)
    {
        return retained.Count == 0
            ? "The scan reported nothing at all."
            : "The scan reported:\n"
              + string.Join("\n", retained.Select(path => $"    [{path.Scan}] {path.ResolvedPath}"));
    }
}
