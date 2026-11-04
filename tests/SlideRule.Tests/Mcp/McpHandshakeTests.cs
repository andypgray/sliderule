using ModelContextProtocol.Client;
using Shouldly;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     Drives the handshake of a client on the SDK's defaults, which probes with <c>server/discover</c> before it
///     falls back to <c>initialize</c>, against the server as <c>McpServerCommand</c> configures it.
/// </summary>
/// <remarks>
///     Every harness start already proves the prompt path, where the server refuses the probe and the client falls
///     back at once; <see cref="McpPipelineHarness.StartAsync" /> asserts the negotiated revision.
/// </remarks>
public sealed class McpHandshakeTests
{
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
