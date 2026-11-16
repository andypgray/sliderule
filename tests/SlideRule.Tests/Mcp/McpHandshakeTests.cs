using ModelContextProtocol.Client;
using Shouldly;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     Drives the handshake against the server as <c>McpServerCommand</c> configures it: a client on the SDK's
///     defaults, which probes with <c>server/discover</c> before it falls back to <c>initialize</c>, and a client
///     that asks for one revision over <c>initialize</c> alone, as Claude Code does.
/// </summary>
public sealed class McpHandshakeTests
{
    [Fact]
    public async Task Connect_ClientOnTheSdkDefaults_NegotiatesTheDiscoveryRevision()
    {
        // Act
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithCleanSpec(),
            Ct);

        // Assert
        harness.Client.NegotiatedProtocolVersion.ShouldBe("2026-07-28");
    }

    [Fact]
    public async Task Connect_ClientOnTheInitializeHandshake_Serves()
    {
        // Arrange: the revision Claude Code asks for, over initialize with no discovery probe.
        McpClientOptions clientOptions = new() { ProtocolVersion = "2025-11-25" };

        // Act
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithCleanSpec(),
            Ct,
            clientOptions: clientOptions);
        IList<McpClientTool> tools = await harness.Client.ListToolsAsync(cancellationToken: Ct);

        // Assert
        tools.Select(tool => tool.Name).ShouldContain("arch_check");
    }

    [Fact]
    public async Task Connect_ProbeReplyArrivesAfterTheProbeTimeout_FallsBackToInitialize()
    {
        // Arrange: the server reads the probe at once, and its reply reaches the client long after the client
        // stopped waiting for it, so the fallback initialize follows a probe the server has already handled.
        McpClientOptions clientOptions = new() { DiscoverProbeTimeout = TimeSpan.FromMilliseconds(100) };

        // Act
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithCleanSpec(),
            Ct,
            clientOptions: clientOptions,
            replyDelay: TimeSpan.FromSeconds(1));
        IList<McpClientTool> tools = await harness.Client.ListToolsAsync(cancellationToken: Ct);

        // Assert: past the handshake, the session serves requests.
        tools.Select(tool => tool.Name).ShouldContain("arch_check");
    }
}
