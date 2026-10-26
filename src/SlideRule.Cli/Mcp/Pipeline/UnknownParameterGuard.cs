using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace SlideRule.Cli.Mcp.Pipeline;

/// <summary>
///     Rejects tool calls whose JSON argument keys disagree with the matched tool's input schema, either way: a key
///     that matches no declared parameter, or a required parameter with no key at all. A silent drop or a binder
///     failure becomes an actionable, self-correcting error.
/// </summary>
/// <remarks>
///     <para>
///         The MCP SDK binds each parameter by looking its name up among the call's arguments and ignores
///         every other key, so a hallucinated key (the model guessing <c>file</c>/<c>ruleId</c> variants
///         instead of the real parameter) is dropped before the tool runs. The model never learns it sent a
///         typo: an optional parameter silently takes its default, and a required one fails inside the binder
///         with an <see cref="ArgumentException" /> that <see cref="GlobalCallToolFilter" /> can only log as
///         unexpected. A call that leaves a required parameter out altogether meets the same exception.
///     </para>
///     <para>
///         It must accept exactly the keys the binder binds, and the SDK publishes that set itself: the properties
///         of the matched tool's <see cref="Tool.InputSchema" />, which leave out every parameter it binds from the
///         request or from services. The same schema's <c>required</c> array lists the parameters the binder
///         cannot default. Keys are compared ordinally, as the SDK's argument dictionary compares them: the Web
///         defaults' case-insensitivity applies only inside a value being deserialized, never to the keys. The
///         SDK's own unmapped-key check stays off because these options keep <c>UnmappedMemberHandling</c> at
///         <c>Skip</c>.
///     </para>
/// </remarks>
internal static class UnknownParameterGuard
{
    /// <summary>
    ///     Returns an error message if <paramref name="arguments" /> holds a key that matches no property of
    ///     <paramref name="tool" />'s input schema, or lacks a key the schema requires, otherwise
    ///     <see langword="null" />. An unknown key is reported in preference to a missing one, since a misspelt key
    ///     usually is the missing one. Only key identity is inspected, so a present-but-null argument counts as
    ///     present and is left to the tool.
    /// </summary>
    internal static string? Validate(Tool tool, IDictionary<string, JsonElement>? arguments)
    {
        List<string> names = ParameterNames(tool);
        string validNames = string.Join(", ", names);

        List<string> unknown = arguments?.Keys
            .Where(key => !names.Contains(key, StringComparer.Ordinal))
            .ToList() ?? [];
        if (unknown.Count > 0) return $"Unknown parameter {Quoted(unknown)} on \"{tool.Name}\". Valid: {validNames}.";

        List<string> missing = RequiredNames(tool)
            .Where(name => arguments?.ContainsKey(name) != true)
            .ToList();
        if (missing.Count > 0) return $"Missing required parameter {Quoted(missing)} on \"{tool.Name}\". Valid: {validNames}.";

        return null;
    }

    /// <summary>The argument keys <paramref name="tool" /> binds, in the order its schema lists them.</summary>
    private static List<string> ParameterNames(Tool tool)
    {
        return tool.InputSchema.TryGetProperty("properties", out JsonElement properties)
               && properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject()
                .Select(property => property.Name)
                .ToList()
            : [];
    }

    /// <summary>The argument keys <paramref name="tool" />'s schema requires, or empty when it requires none.</summary>
    private static List<string> RequiredNames(Tool tool)
    {
        return tool.InputSchema.TryGetProperty("required", out JsonElement required)
               && required.ValueKind == JsonValueKind.Array
            ? required.EnumerateArray()
                .Select(name => name.GetString()!)
                .ToList()
            : [];
    }

    private static string Quoted(IEnumerable<string> names)
    {
        return string.Join(", ", names.Select(name => $"\"{name}\""));
    }
}
