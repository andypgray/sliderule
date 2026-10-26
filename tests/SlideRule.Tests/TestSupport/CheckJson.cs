using System.Text.Json;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Readers over a check verb's <c>--json</c> document — the product's documented wire format, so a test
///     asking what a rule reported reads it exactly as a consumer would.
/// </summary>
internal static class CheckJson
{
    /// <summary>One rule's object, cloned so it outlives the parse.</summary>
    internal static JsonElement Rule(string checkJson, string ruleId)
    {
        using JsonDocument document = JsonDocument.Parse(checkJson);
        return Rule(document, ruleId);
    }

    /// <summary>
    ///     The same, over a document a caller already holds for its other reads. Throws like LINQ's
    ///     <c>Single</c> when the report does not carry the rule exactly once, naming the rule asked for and
    ///     every rule the report carries.
    /// </summary>
    internal static JsonElement Rule(JsonDocument document, string ruleId)
    {
        List<JsonElement> rules = document.RootElement.GetProperty("rules")
            .EnumerateArray()
            .ToList();
        List<JsonElement> matching = rules
            .Where(rule => rule.GetProperty("id")
                .GetString() == ruleId)
            .ToList();
        if (matching.Count == 1) return matching[0].Clone();

        string carries = matching.Count == 0 ? "no rule" : $"{matching.Count} rules";
        string present = string.Join(", ", rules.Select(rule => rule.GetProperty("id")
            .GetString()));
        throw new InvalidOperationException($"the report carries {carries} '{ruleId}'. It reported: {present}");
    }

    /// <summary>One rule's violations, in report order, each cloned so it outlives the parse.</summary>
    internal static IReadOnlyList<JsonElement> Violations(string checkJson, string ruleId)
    {
        return Rule(checkJson, ruleId)
            .GetProperty("violations")
            .EnumerateArray()
            .Select(violation => violation.Clone())
            .ToList();
    }

    /// <summary>
    ///     The string array at a top-level <paramref name="slot" /> of a check document still in its wire form:
    ///     <c>workspaceDiagnostics</c>, <c>failedProjects</c>, <c>uncheckedProjects</c>,
    ///     <c>restoreFailedProjects</c>, in document order.
    /// </summary>
    internal static IReadOnlyList<string> Strings(string checkJson, string slot)
    {
        using JsonDocument document = JsonDocument.Parse(checkJson);
        return document.RootElement.Strings(slot);
    }
}
