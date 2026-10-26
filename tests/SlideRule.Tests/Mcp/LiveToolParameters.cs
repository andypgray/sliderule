using System.Reflection;
using SlideRule.Cli.Mcp.Pipeline;

namespace SlideRule.Tests.Mcp;

/// <summary>The parameters the live server binds from a <c>tools/call</c> argument object, read by reflection.</summary>
internal static class LiveToolParameters
{
    /// <summary>
    ///     Every tool the server registers, by its wire name, with the parameters it binds from the argument
    ///     object in declaration order. A tool that binds none is listed with an empty list.
    /// </summary>
    internal static IReadOnlyList<(string Tool, IReadOnlyList<ParameterInfo> Parameters)> ByTool()
    {
        return ToolAttributeDiscovery.GetNamedToolMethods()
            .Select(static tool => (Tool: tool.Name, Parameters: JsonBound(tool.Method)))
            .ToList();
    }

    private static IReadOnlyList<ParameterInfo> JsonBound(MethodInfo method)
    {
        return method.GetParameters()
            .Where(ToolAttributeDiscovery.IsJsonBoundParameter)
            .ToList();
    }
}
