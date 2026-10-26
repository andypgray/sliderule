using System.Reflection;
using ModelContextProtocol.Server;
using Shouldly;
using SlideRule.Cli.Mcp.Pipeline;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     <see cref="ToolAttributeDiscovery" />: the one definition of which methods the server registers as tools.
/// </summary>
public sealed class ToolAttributeDiscoveryTests
{
    [Fact]
    public void GetToolMethods_HoldsOnlyAttributedTools()
    {
        // Every tool type also exposes object's public Equals, GetHashCode, ToString and GetType; a
        // consumer that met them would count them as tools, or their parameters as live ones.
        ToolAttributeDiscovery.GetToolMethods()
            .ShouldAllBe(method => method.GetCustomAttribute<McpServerToolAttribute>() != null);
    }
}
