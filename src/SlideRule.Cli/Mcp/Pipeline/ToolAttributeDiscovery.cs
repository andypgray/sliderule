using System.Reflection;
using ModelContextProtocol.Server;

namespace SlideRule.Cli.Mcp.Pipeline;

/// <summary>Reflects over the current assembly to discover the MCP tool methods the server registers.</summary>
internal static class ToolAttributeDiscovery
{
    // One assembly-wide reflection pass for the process: the tool surface is fixed at compile time, and each ask
    // would otherwise be a full GetTypes() walk of every type in the CLI assembly.
    private static readonly MethodInfo[] ToolMethods = typeof(ToolAttributeDiscovery).Assembly
        .GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
        .ToArray();

    /// <summary>The tool methods, in the order they register.</summary>
    internal static IEnumerable<MethodInfo> GetToolMethods()
    {
        return ToolMethods;
    }
}
