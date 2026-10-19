using System.Text.Json;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Readers over a graph verb's <c>--json</c> survey — the product's documented wire format, so a test
///     asking what the survey said about a project reads it exactly as a consumer would.
/// </summary>
/// <remarks>
///     The elements come back uncloned, as the readers here only reach into a document the caller is
///     holding open for its other reads — the shape every survey row is written in.
/// </remarks>
internal static class GraphSurveyJson
{
    /// <summary>One project's row, found by the name the survey lists it under.</summary>
    internal static JsonElement SurveyProject(this JsonDocument document, string name)
    {
        return document.RootElement.GetProperty("projects")
            .EnumerateArray()
            .Single(project => project.GetProperty("name")
                .GetString() == name);
    }

    /// <summary>Every project reference edge, as the pair it joins and how many references cross it.</summary>
    internal static IReadOnlyList<(string Source, string Target, int References)> ProjectEdges(
        this JsonDocument document)
    {
        return document.RootElement.GetProperty("projectEdges")
            .EnumerateArray()
            .Select(edge => (
                edge.GetProperty("source")
                    .GetString()!,
                edge.GetProperty("target")
                    .GetString()!,
                edge.GetProperty("references")
                    .GetInt32()))
            .ToList();
    }
}
