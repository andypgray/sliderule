using Shouldly;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The Shouldly surface over where one fragment of a rendered text sits relative to another: the plain-text
///     twin of <see cref="JsonKeyOrderAssertions" />, for the reports, cards and messages whose line order is the
///     claim.
/// </summary>
/// <remarks>
///     Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///     <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
/// </remarks>
internal static class TextOrderAssertions
{
    /// <summary>
    ///     Asserts <paramref name="text" /> carries both fragments and that the first occurrence of
    ///     <paramref name="earlier" /> comes before the first occurrence of <paramref name="later" />. A red shows
    ///     <paramref name="customMessage" /> where one is given, and the text otherwise.
    /// </summary>
    // Asserted present first: IndexOf answers -1 for a fragment that is not there, and -1 precedes every real
    // position, so an order claim over a dropped line would pass without having tested it.
    internal static void ShouldContainInOrder(
        this string text, string earlier, string later, string? customMessage = null)
    {
        string shown = customMessage ?? text;
        int earlierAt = text.IndexOf(earlier, StringComparison.Ordinal);
        int laterAt = text.IndexOf(later, StringComparison.Ordinal);

        earlierAt.ShouldBeGreaterThanOrEqualTo(0, $"the text has no \"{earlier}\":\n{shown}");
        laterAt.ShouldBeGreaterThanOrEqualTo(0, $"the text has no \"{later}\":\n{shown}");
        earlierAt.ShouldBeLessThan(laterAt, $"\"{earlier}\" must come before \"{later}\":\n{shown}");
    }
}
