using System.Globalization;
using Microsoft.CodeAnalysis;
using Shouldly;

namespace SlideRule.Tests;

/// <summary>
///     The Shouldly surface both analyzers' tests share: which warnings came back, what each one says and
///     carries as properties, and which descriptors an analyzer declares.
/// </summary>
/// <remarks>
///     Every failure message carries the whole reported set — each warning's ID, position and message —
///     because the first question a red raises is what the analyzer actually said, and a count answers
///     none of it. Deliberately not attributed <c>[ShouldlyMethods]</c>, for the reason
///     <c>RuleResultAssertions</c> records: leaving it off makes an inner failure name the read under test
///     rather than the caller's local.
/// </remarks>
internal static class DiagnosticAssertions
{
    /// <summary>Asserts exactly one warning carries <paramref name="id" />, and returns it.</summary>
    internal static Diagnostic ShouldHaveSingle(this IReadOnlyList<Diagnostic> reported, string id)
    {
        List<Diagnostic> matching = reported.Where(diagnostic => diagnostic.Id == id)
            .ToList();

        return matching.ShouldHaveSingleItem(Describe(reported, $"Looking for exactly one {id}"));
    }

    /// <summary>
    ///     Asserts the analyzer reported exactly one warning and that it carries <paramref name="id" /> —
    ///     the shape of every fact that also claims nothing else was said.
    /// </summary>
    internal static Diagnostic ShouldBeTheOnly(this IReadOnlyList<Diagnostic> reported, string id)
    {
        reported.ShouldReport(id);

        return reported.ShouldHaveSingle(id);
    }

    /// <summary>Asserts the analyzer reported nothing at all.</summary>
    internal static void ShouldBeSilent(this IReadOnlyList<Diagnostic> reported)
    {
        reported.ShouldBeEmpty(Describe(reported, "Expected the analyzer to report nothing"));
    }

    /// <summary>Asserts the warnings are exactly <paramref name="ids" />, in the order they were reported.</summary>
    internal static void ShouldReport(this IReadOnlyList<Diagnostic> reported, params string[] ids)
    {
        List<string> declared = reported.Select(diagnostic => diagnostic.Id)
            .ToList();

        declared.ShouldBe(ids, Case.Sensitive, Describe(reported, $"Expected {string.Join(", ", ids)}"));
    }

    /// <summary>
    ///     Asserts no warning carries <paramref name="id" />, whatever else was reported, with
    ///     <paramref name="customMessage" /> heading the red when one is given.
    /// </summary>
    internal static void ShouldNotReport(
        this IReadOnlyList<Diagnostic> reported, string id, string? customMessage = null)
    {
        List<Diagnostic> matching = reported.Where(diagnostic => diagnostic.Id == id)
            .ToList();
        string header = customMessage ?? $"Expected no {id}";

        matching.ShouldBeEmpty(Describe(reported, header));
    }

    /// <summary>Asserts the whole message, composed in the invariant culture.</summary>
    internal static void ShouldSay(this Diagnostic diagnostic, string message)
    {
        diagnostic.GetMessage(CultureInfo.InvariantCulture)
            .ShouldBe(message);
    }

    /// <summary>
    ///     Asserts the warning's properties are exactly <paramref name="expected" />, each written
    ///     <c>key=value</c> and ordered by key — so a key that appears, disappears or changes value reds.
    /// </summary>
    internal static void ShouldCarry(this Diagnostic diagnostic, params string[] expected)
    {
        List<string> carried = diagnostic.Properties
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => $"{property.Key}={property.Value}")
            .ToList();

        carried.ShouldBe(expected);
    }

    /// <summary>
    ///     Asserts exactly one of the <paramref name="declared" /> descriptors carries <paramref name="id" />,
    ///     and returns it.
    /// </summary>
    internal static DiagnosticDescriptor ShouldDeclare(this IEnumerable<DiagnosticDescriptor> declared, string id)
    {
        List<DiagnosticDescriptor> all = declared.ToList();
        List<DiagnosticDescriptor> matching = all
            .Where(descriptor => descriptor.Id == id)
            .ToList();
        string ids = string.Join(", ", all.Select(descriptor => descriptor.Id));

        return matching.ShouldHaveSingleItem($"Looking for descriptor '{id}'; the analyzer declares: {ids}.");
    }

    /// <summary>The value the warning carries as a property under <paramref name="key" />, or null if none.</summary>
    internal static string? Property(this Diagnostic diagnostic, string key)
    {
        return diagnostic.Properties.TryGetValue(key, out string? value) ? value : null;
    }

    /// <summary>A failure message: <paramref name="header" />, then every warning reported, one per line.</summary>
    internal static string Describe(IReadOnlyList<Diagnostic> reported, string header)
    {
        if (reported.Count == 0) return $"{header}; the analyzer reported nothing.";

        IEnumerable<string> lines = reported.Select(Describe);

        return $"{header}; the analyzer reported:\n{string.Join("\n", lines)}";
    }

    private static string Describe(Diagnostic diagnostic)
    {
        // A warning with no location has a line span whose path is null rather than empty, which is worth
        // knowing here: composing this message is the first thing every red does.
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        string place = string.IsNullOrEmpty(span.Path)
            ? "(no location)"
            : $"{span.Path}:{span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}";

        return $"  {diagnostic.Id} at {place}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}";
    }
}
