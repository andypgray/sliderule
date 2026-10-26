using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using SlideRule.Cli.Mcp.Pipeline;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     The tools the live server registers and the parameters each binds from a <c>tools/call</c> argument object,
///     read by reflection.
/// </summary>
internal static class LiveToolParameters
{
    // An unnamed tool would still be registered, under the name the SDK derives, but carries no name a gate could
    // key it by.
    private static readonly (string Name, MethodInfo Method)[] NamedTools = ToolAttributeDiscovery.GetToolMethods()
        .Where(static method => method.GetCustomAttribute<McpServerToolAttribute>()!.Name is not null)
        .Select(static method => (method.GetCustomAttribute<McpServerToolAttribute>()!.Name!, method))
        .ToArray();

    /// <summary>The tool methods whose attribute carries a name, with that name, in registration order.</summary>
    internal static IReadOnlyList<(string Name, MethodInfo Method)> NamedToolMethods()
    {
        return NamedTools;
    }

    /// <summary>
    ///     Every tool the server registers, by its wire name, with the parameters it binds from the argument
    ///     object in declaration order. A tool that binds none is listed with an empty list.
    /// </summary>
    internal static IReadOnlyList<(string Tool, IReadOnlyList<ParameterInfo> Parameters)> ByTool()
    {
        return NamedTools
            .Select(static tool => (Tool: tool.Name, Parameters: JsonBound(tool.Method)))
            .ToList();
    }

    /// <summary>
    ///     True when a parameter is bound from JSON arguments (and thus part of the advertised schema), false when
    ///     the SDK binds it from request context or DI. The excluded types mirror the SDK's own augmentation set, so
    ///     a context-bound parameter cannot become a false positive.
    /// </summary>
    internal static bool IsJsonBound(ParameterInfo p)
    {
        Type t = p.ParameterType;

        if (t == typeof(CancellationToken) || t == typeof(AIFunctionArguments)) return false;

        if (typeof(IServiceProvider).IsAssignableFrom(t) || typeof(McpServer).IsAssignableFrom(t)) return false;

        if (t.IsGenericType)
        {
            Type definition = t.GetGenericTypeDefinition();
            if (definition == typeof(RequestContext<>) || definition == typeof(IProgress<>)) return false;
        }

        if (p.GetCustomAttribute<FromKeyedServicesAttribute>() is not null) return false;

        return p.Name is not null;
    }

    private static IReadOnlyList<ParameterInfo> JsonBound(MethodInfo method)
    {
        return method.GetParameters()
            .Where(IsJsonBound)
            .ToList();
    }
}
