using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace SlideRule.Cli.Mcp.Pipeline;

/// <summary>
///     Rejects tool calls that carry a JSON argument key matching no declared parameter,
///     turning a silent drop into an actionable, self-correcting error.
/// </summary>
/// <remarks>
///     <para>
///         The MCP SDK binds each parameter by looking its name up among the call's arguments and ignores
///         every other key, so a hallucinated key (the model guessing <c>file</c>/<c>ruleId</c> variants
///         instead of the real parameter) is dropped before the tool runs. The model never learns it sent a
///         typo: a required parameter fails downstream as missing, and an optional one silently takes its
///         default.
///     </para>
///     <para>
///         It must accept exactly the keys the binder binds. Names are therefore read from
///         <see cref="ParameterInfo.Name" /> with no naming policy applied, as the binder reads them for a
///         parameter no experimental <c>[AIParameterName]</c> renames; the Web defaults' case-insensitivity
///         applies only inside a value being deserialized, never to the keys. Parameters the SDK binds from
///         the request or from services are left out (<see cref="ToolAttributeDiscovery.IsJsonBoundParameter" />).
///         The SDK's own unmapped-key check stays off because these options keep
///         <c>UnmappedMemberHandling</c> at <c>Skip</c>; custom converters are not the custom parameter
///         binding that would also switch it off.
///     </para>
/// </remarks>
internal static class UnknownParameterGuard
{
    private static readonly FrozenDictionary<string, string[]> ToolParameters = BuildMap();

    /// <summary>
    ///     Returns an error message if <paramref name="arguments" /> holds a key that matches no declared parameter
    ///     of <paramref name="toolName" />, otherwise <see langword="null" />. Only key identity is inspected, so a
    ///     present-but-null valid argument is fine.
    /// </summary>
    internal static string? Validate(string toolName, IDictionary<string, JsonElement>? arguments)
    {
        // Unknown tool name is the SDK's dispatch concern, not ours — never block it here.
        if (!ToolParameters.TryGetValue(toolName, out string[]? names)) return null;

        if (arguments is null || arguments.Count == 0) return null;

        List<string>? unknown = null;
        foreach (string key in arguments.Keys)
            if (!names.Contains(key, StringComparer.Ordinal))
                (unknown ??= []).Add(key);

        if (unknown is null) return null;

        string badKeys = string.Join(", ", unknown.Select(k => $"\"{k}\""));
        string validNames = string.Join(", ", names);
        return $"Unknown parameter {badKeys} on \"{toolName}\". Valid: {validNames}.";
    }

    private static FrozenDictionary<string, string[]> BuildMap()
    {
        // Ordinal throughout: the SDK dispatches a call through a tool collection keyed ordinally and binds
        // arguments from a dictionary compared ordinally, so a tool name or key that differs only in case
        // reaches nothing. Matching the same way is what lets the guard name such a key rather than wave it
        // through, and leaves a wrong-case tool name to the SDK's own unknown-tool answer.
        Dictionary<string, string[]> map = new(StringComparer.Ordinal);

        foreach ((string toolName, MethodInfo method) in ToolAttributeDiscovery.GetNamedToolMethods())
        {
            string[] orderedNames = method.GetParameters()
                .Where(ToolAttributeDiscovery.IsJsonBoundParameter)
                .Select(p => p.Name!)
                .ToArray();

            map[toolName] = orderedNames;
        }

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
