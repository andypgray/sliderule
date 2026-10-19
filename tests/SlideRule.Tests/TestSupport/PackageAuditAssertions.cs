using System.Text.Json;
using Shouldly;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The Shouldly surface over NuGet audit advisories: the <c>packageAudit</c> key of a check, status or survey
///     document, and the entries the grouping produces before any document is written.
/// </summary>
/// <remarks>
///     <para>
///         Every failure message carries the whole subject — the document, or every entry the grouping
///         produced — because the first thing a red here raises is what the run reported instead.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
internal static class PackageAuditAssertions
{
    private const string Key = "packageAudit";
    private const string CountKey = "packageAuditCount";

    /// <summary>
    ///     Asserts <paramref name="document" /> carries exactly one advisory entry, saying
    ///     <paramref name="message" /> and naming exactly <paramref name="projects" />, in that order.
    /// </summary>
    internal static void ShouldCarryPackageAudit(this string document, string message, IReadOnlyList<string> projects)
    {
        using JsonDocument parsed = CheckJsonAssertions.ParseOrFail(document);
        parsed.ShouldCarryPackageAudit(message, projects);
    }

    /// <summary>The same, over a document a caller already holds for its other reads.</summary>
    internal static void ShouldCarryPackageAudit(
        this JsonDocument document, string message, IReadOnlyList<string> projects)
    {
        string report = Describe(document);
        JsonElement root = document.RootElement;

        root.TryGetProperty(CountKey, out _)
            .ShouldBeFalse(report);
        root.TryGetProperty(Key, out JsonElement entries)
            .ShouldBeTrue(report);
        JsonElement entry = entries.EnumerateArray()
            .ShouldHaveSingleItem(report);
        entry.GetProperty("message")
            .GetString()
            .ShouldBe(message, report);
        entry.Strings("projects")
            .ShouldBe(projects, customMessage: report);
    }

    /// <summary>
    ///     Asserts <paramref name="document" /> elided its advisories to <paramref name="count" /> entries:
    ///     <c>packageAuditCount</c> present and saying so, <c>packageAudit</c> absent.
    /// </summary>
    internal static void ShouldElidePackageAuditTo(this string document, int count)
    {
        using JsonDocument parsed = CheckJsonAssertions.ParseOrFail(document);
        parsed.ShouldElidePackageAuditTo(count);
    }

    /// <summary>The same, over a document a caller already holds for its other reads.</summary>
    internal static void ShouldElidePackageAuditTo(this JsonDocument document, int count)
    {
        string report = Describe(document);
        JsonElement root = document.RootElement;

        root.TryGetProperty(Key, out _)
            .ShouldBeFalse(report);
        root.TryGetProperty(CountKey, out JsonElement elided)
            .ShouldBeTrue(report);
        elided.GetInt32()
            .ShouldBe(count, report);
    }

    /// <summary>Asserts <paramref name="document" /> carries neither advisory key — a run that raised none.</summary>
    internal static void ShouldCarryNoPackageAudit(this string document)
    {
        using JsonDocument parsed = CheckJsonAssertions.ParseOrFail(document);
        parsed.ShouldCarryNoPackageAudit();
    }

    /// <summary>The same, over a document a caller already holds for its other reads.</summary>
    internal static void ShouldCarryNoPackageAudit(this JsonDocument document)
    {
        string report = Describe(document);
        JsonElement root = document.RootElement;

        root.TryGetProperty(Key, out _)
            .ShouldBeFalse(report);
        root.TryGetProperty(CountKey, out _)
            .ShouldBeFalse(report);
    }

    /// <summary>
    ///     Asserts the grouping produced exactly <paramref name="expected" />, in order: each entry's message,
    ///     and its projects — or none, where the expected projects are null.
    /// </summary>
    internal static void ShouldHaveEntries(
        this IReadOnlyList<PackageAuditEntry> actual, params (string Message, string[]? Projects)[] expected)
    {
        string report = Describe(actual);

        actual.Count.ShouldBe(expected.Length, report);
        foreach ((PackageAuditEntry entry, (string Message, string[]? Projects) wanted) in actual.Zip(expected))
        {
            entry.Message.ShouldBe(wanted.Message, report);
            entry.Projects.ShouldBe(wanted.Projects, customMessage: report);
        }
    }

    private static string Describe(JsonDocument document)
    {
        return $"The document:{Environment.NewLine}{document.RootElement.GetRawText()}";
    }

    private static string Describe(IReadOnlyList<PackageAuditEntry> entries)
    {
        if (entries.Count == 0) return "The grouping produced no entries.";

        IEnumerable<string> lines = entries.Select(entry => entry.Projects is null
            ? $"  {entry.Message} (no projects)"
            : $"  {entry.Describe()}");
        return $"The grouping produced:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }
}
