using System.Diagnostics;
using Shouldly;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     The deployment-shaped net under the MCP server: the shipped <c>sliderule-cli.dll</c> run as a real
///     child process, spoken to over real stdio, with the client's stdin pipe held open for the whole
///     call — exactly what an MCP client does, and a condition no in-process MCP suite here can
///     reproduce.
/// </summary>
/// <remarks>
///     <para>
///         <c>arch_check</c> with a <c>diffBase</c> shells git. A child started
///         without its own redirected stdin inherits a duplicate of this server's, which under a live
///         client is the JSON-RPC pipe the stdio transport is permanently parked on in a synchronous
///         read; Git for Windows probes its standard handles at startup and that probe blocks forever
///         against such a pipe, so the child never reaches its own entry point. No in-process suite can
///         see it: <see cref="McpPipelineHarness" /> drives the server over memory pipes and the CLI
///         inherits a stdin that is already at EOF. Only a real child over real stdio sees it, so this
///         test spawns one.
///     </para>
///     <para>
///         The conversation and its budgets live in
///         <see cref="McpChildHarness.ConverseAsync" />, whose defaults cover a cold child, so a wedge
///         shows up as the id:2 response never arriving rather than as an indefinite hang.
///     </para>
///     <para>
///         <see cref="McpChildServerRepoHandleTests" /> drives the same conversation over the same
///         plumbing, so the setups are near-identical, but every one of its facts opens with
///         <c>ProcessFileFootprint.SkipUnlessSupported()</c>, and the footprint scan
///         is Windows-x64 only. CI runs ubuntu and macos legs too, and on those this is the only
///         real-child-over-real-stdio coverage there is. Deleting it would leave the stdin-inheritance
///         wedge unguarded on two of four legs.
///     </para>
/// </remarks>
public sealed class McpStdioChildServerTests
{
    [Fact]
    public async Task ArchCheckWithDiffBase_OverRealStdio_ReturnsTheReport()
    {
        using var repo = new TempGitRepo();
        // A brand-new untracked file in dragon territory, so the report can only carry the tripwire if git
        // actually ran and produced a diff — the assertion the wedge fails.
        repo.WriteQuarantineNote();

        ProcessStartInfo startInfo = McpChildHarness.ServerStartInfo(
            TestsBinCli.Dll(),
            repo.SolutionPath,
            CliRunner.QuarantinedSpecDll,
            repo.Root);

        ChildConversation conversation = await McpChildHarness.ConverseAsync(
            startInfo, [("arch_check", """{"diffBase":"HEAD"}""")]);

        conversation.Handshake.ShouldNotBeNull(
            $"the MCP server never answered `initialize` over real stdio.\nstderr:\n{conversation.Diagnostics}");
        string? response = conversation.Answers.GetValueOrDefault("arch_check");
        response.ShouldNotBeNull(
            "the arch_check response never arrived. A child process that inherits the server's live stdin "
            + "pipe wedges at startup and the call cannot complete — the failure no in-process MCP test can "
            + $"see.\nstderr:\n{conversation.Diagnostics}");

        string text = response.ShouldHaveToolText("arch_check");
        McpChildHarness.IsToolError(response)
            .ShouldBeFalse($"arch_check reported a tool error: {text}");

        // The tripwire proves the whole diff path ran: git resolved the toplevel, listed the untracked
        // file, and the checker matched it against the quarantined scope.
        text.ShouldContain("quarantinedScopeTouched");
    }
}
