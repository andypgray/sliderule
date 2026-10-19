using System.Text.Json;

namespace SlideRule.Tests.TestSupport;

/// <summary>Reads of a string or a string array off one object of a JSON document.</summary>
internal static class JsonElementReads
{
    /// <summary>
    ///     The string array at <paramref name="property" /> of <paramref name="element" />, in document order,
    ///     with a null entry read as an empty string.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The element carries no <paramref name="property" />.</exception>
    internal static IReadOnlyList<string> Strings(this JsonElement element, string property)
    {
        return element.GetProperty(property)
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToList();
    }

    /// <summary>
    ///     The string at <paramref name="property" /> of <paramref name="element" />, or null when the element
    ///     does not carry it, so an optional slot the wire document omits reads as absent rather than throwing.
    /// </summary>
    internal static string? StringOrDefault(this JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;
    }
}
