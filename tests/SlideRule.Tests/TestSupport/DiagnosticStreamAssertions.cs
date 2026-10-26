using System.Text.Json;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The Shouldly surface over the load's diagnostic stream: the <c>workspaceDiagnostics</c> key of a check or
///     survey document, and the <c>workspaceDiagnosticCount</c> twin the floor rung writes in its place.
/// </summary>
/// <remarks>
///     Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///     <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
/// </remarks>
internal static class DiagnosticStreamAssertions
{
    private const string Key = "workspaceDiagnostics";
    private const string CountKey = "workspaceDiagnosticCount";

    /// <summary>
    ///     Asserts <paramref name="document" /> elided its diagnostic stream to <paramref name="count" /> lines:
    ///     <c>workspaceDiagnosticCount</c> present and saying so, <c>workspaceDiagnostics</c> absent.
    /// </summary>
    internal static void ShouldElideDiagnosticStreamTo(this string document, int count)
    {
        using JsonDocument parsed = CheckJsonAssertions.ShouldParseAsJson(document);
        parsed.ShouldElideSlotTo(Key, CountKey, count);
    }

    /// <summary>Asserts <paramref name="document" /> carries its diagnostic stream whole, with no count beside it.</summary>
    internal static void ShouldKeepDiagnosticStreamWhole(this string document)
    {
        using JsonDocument parsed = CheckJsonAssertions.ShouldParseAsJson(document);
        parsed.ShouldKeepSlotWhole(Key, CountKey);
    }

    /// <summary>
    ///     Asserts <paramref name="document" /> carries neither stream key, as a survey of a load with nothing on its
    ///     stream does. A check document writes an empty stream as an empty list instead.
    /// </summary>
    internal static void ShouldCarryNoDiagnosticStream(this string document)
    {
        using JsonDocument parsed = CheckJsonAssertions.ShouldParseAsJson(document);
        parsed.ShouldCarryNeitherSlot(Key, CountKey);
    }
}
