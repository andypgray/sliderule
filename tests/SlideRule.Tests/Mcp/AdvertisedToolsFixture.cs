using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     Provides one MCP pipeline and one <c>tools/list</c> per test class, for a class that only reads the
///     advertised tool surface.
/// </summary>
/// <remarks>
///     Every case in such a class reads the same response, so a harness per case would start a server per case
///     to read it. The list is read uncancellably, because a fixture's work is shared
///     by every case in the class.
/// </remarks>
public sealed class AdvertisedToolsFixture : IAsyncLifetime
{
    private McpPipelineHarness? _harness;

    /// <summary>Gets the tools the server advertises over <c>tools/list</c>, read once.</summary>
    public IList<McpClientTool> Tools { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        _harness = await McpPipelineHarness.StartAsync(McpServerBindings.MyAppWithCleanSpec(), CancellationToken.None);
        Tools = await _harness.Client.ListToolsAsync(cancellationToken: CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    /// <summary>Returns the advertised tool named <paramref name="name" />, asserting that one is listed.</summary>
    public Tool ShouldHaveTool(string name)
    {
        McpClientTool? tool = Tools.SingleOrDefault(candidate => candidate.ProtocolTool.Name == name);
        tool.ShouldNotBeNull(
            $"no advertised tool is named '{name}'; listed: {string.Join(", ", Tools.Select(candidate => candidate.ProtocolTool.Name))}");

        return tool.ProtocolTool;
    }
}
