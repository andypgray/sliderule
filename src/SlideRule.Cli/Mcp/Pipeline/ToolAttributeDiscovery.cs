using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace SlideRule.Cli.Mcp.Pipeline;

/// <summary>
///     Reflects over the current assembly to discover MCP tool methods, and answers which of their
///     parameters the server binds from JSON — the one definition of "the tool surface" every part of the
///     pipeline reasons against.
/// </summary>
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

    // An unnamed tool would still be registered, under the name the SDK derives, but carries no name the guard
    // and the gates could key it by.
    private static readonly (string Name, MethodInfo Method)[] NamedToolMethods = ToolMethods
        .Where(method => method.GetCustomAttribute<McpServerToolAttribute>()!.Name is not null)
        .Select(method => (method.GetCustomAttribute<McpServerToolAttribute>()!.Name!, method))
        .ToArray();

    /// <summary>The tool methods, in the order they register.</summary>
    internal static IEnumerable<MethodInfo> GetToolMethods()
    {
        return ToolMethods;
    }

    /// <summary>The tool methods whose attribute carries a name, with that name, in registration order.</summary>
    internal static IReadOnlyList<(string Name, MethodInfo Method)> GetNamedToolMethods()
    {
        return NamedToolMethods;
    }

    /// <summary>
    ///     True when a parameter is bound from JSON arguments (and thus part of the advertised schema), false when
    ///     the SDK binds it from request context or DI. The excluded types mirror the SDK's own augmentation set, so
    ///     a context-bound parameter cannot become a false positive.
    /// </summary>
    internal static bool IsJsonBoundParameter(ParameterInfo p)
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
}
