using System.Text.Json;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The <c>unsupportedProjects</c> slot of a <c>graph</c>, <c>check</c> or <c>status</c> <c>--json</c> document,
///     which all three write in one shape.
/// </summary>
internal static class UnsupportedProjectsJson
{
    /// <summary>
    ///     Each entry as its <c>project</c> and <c>reason</c>, in document order; a document without the slot reads
    ///     as none.
    /// </summary>
    internal static IReadOnlyList<(string Project, string Reason)> UnsupportedProjectEntries(this JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("unsupportedProjects", out JsonElement entries)) return [];

        return entries
            .EnumerateArray()
            .Select(entry => (entry.GetProperty("project").GetString()!, entry.GetProperty("reason").GetString()!))
            .ToList();
    }
}
