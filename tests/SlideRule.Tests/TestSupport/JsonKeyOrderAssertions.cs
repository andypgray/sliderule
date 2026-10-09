using Shouldly;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The Shouldly surface over a rendered document's key order, read as text: which slot serializes ahead
///     of which, for the documents whose order is the claim — what a reader with a response budget keeps
///     when the cut lands, and what it loses first.
/// </summary>
/// <remarks>
///     Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///     <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
/// </remarks>
internal static class JsonKeyOrderAssertions
{
    /// <summary>
    ///     Asserts <paramref name="document" /> carries both keys and that <paramref name="earlier" />
    ///     serializes ahead of <paramref name="later" />.
    /// </summary>
    internal static void ShouldSerializeBefore(this string document, string earlier, string later)
    {
        int earlierAt = document.ShouldHaveKeyAt(earlier);
        int laterAt = document.ShouldHaveKeyAt(later);

        earlierAt.ShouldBeLessThan(laterAt, $"\"{earlier}\" must serialize before \"{later}\":\n{document}");
    }

    /// <summary>
    ///     Asserts <paramref name="document" /> carries <paramref name="key" />, and hands back where its
    ///     first occurrence sits.
    /// </summary>
    // Asserted present first: IndexOf answers -1 for a key that is not in the document and -1 precedes every
    // real position, so an ordering assertion over a stamp the renderer had dropped would pass without ever
    // having tested it.
    internal static int ShouldHaveKeyAt(this string document, string key)
    {
        int at = document.IndexOf($"\"{key}\":", StringComparison.Ordinal);

        at.ShouldBeGreaterThanOrEqualTo(0, $"the document has no \"{key}\" key:\n{document}");

        return at;
    }
}
