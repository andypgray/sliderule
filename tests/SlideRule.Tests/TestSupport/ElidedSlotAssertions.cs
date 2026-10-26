using System.Text.Json;
using Shouldly;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The Shouldly surface over a list slot the floor rung replaces with a count twin, which is written only where the
///     list had entries and never as a bare 0: the slot is whole, elided to its count, or absent with no count.
/// </summary>
/// <remarks>
///     <para>
///         Every failure message carries the whole document, because the first thing a red here raises is which
///         state the document was in instead.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
internal static class ElidedSlotAssertions
{
    /// <summary>
    ///     Asserts <paramref name="document" /> elided <paramref name="key" /> to <paramref name="count" /> entries:
    ///     <paramref name="countKey" /> present and saying so, <paramref name="key" /> absent.
    /// </summary>
    internal static void ShouldElideSlotTo(this JsonDocument document, string key, string countKey, int count)
    {
        string report = Describe(document);
        JsonElement root = document.RootElement;

        root.TryGetProperty(key, out _)
            .ShouldBeFalse(report);
        root.TryGetProperty(countKey, out JsonElement elided)
            .ShouldBeTrue(report);
        elided.GetInt32()
            .ShouldBe(count, report);
    }

    /// <summary>
    ///     Asserts <paramref name="document" /> carries <paramref name="key" /> whole: the list present and no
    ///     <paramref name="countKey" /> beside it.
    /// </summary>
    internal static void ShouldKeepSlotWhole(this JsonDocument document, string key, string countKey)
    {
        string report = Describe(document);
        JsonElement root = document.RootElement;

        root.TryGetProperty(key, out _)
            .ShouldBeTrue(report);
        root.TryGetProperty(countKey, out _)
            .ShouldBeFalse(report);
    }

    /// <summary>
    ///     Asserts <paramref name="document" /> carries neither <paramref name="key" /> nor
    ///     <paramref name="countKey" />.
    /// </summary>
    internal static void ShouldCarryNeitherSlot(this JsonDocument document, string key, string countKey)
    {
        string report = Describe(document);
        JsonElement root = document.RootElement;

        root.TryGetProperty(key, out _)
            .ShouldBeFalse(report);
        root.TryGetProperty(countKey, out _)
            .ShouldBeFalse(report);
    }

    private static string Describe(JsonDocument document)
    {
        return $"The document:{Environment.NewLine}{document.RootElement.GetRawText()}";
    }
}
