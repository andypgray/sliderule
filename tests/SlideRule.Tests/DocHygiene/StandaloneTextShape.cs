using System.Text.RegularExpressions;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Measures a short text that a reader meets on its own, away from any page: a package description, or a
///     manifest string a registry client shows at install time. Such a text is held as strictly as the landing
///     page's first screen, and to the contrast frames and the stock-phrase, house-voice and internal-reference
///     lexicons the reader docs are held to.
/// </summary>
internal static class StandaloneTextShape
{
    /// <summary>The line number a gate's finding opens with, which is always the first for a standalone text.</summary>
    private static readonly Regex LeadingLineNumber = new(@"^L?\d+: ");

    /// <summary>
    ///     Every way <paramref name="text" /> departs from plain declaratives, one finding per departure, each without
    ///     the line number the underlying gate opens it with.
    /// </summary>
    internal static IEnumerable<string> Findings(string text)
    {
        return FirstScreenShape.Findings(text)
            .Concat(ProseCadence.FrameFindings(text))
            .Concat(DocProse.FindForbidden(text, DocHygieneTests.StockPhrasePatterns))
            .Concat(DocProse.FindForbidden(text, DocHygieneTests.HouseVoicePatterns))
            .Concat(DocProse.FindForbidden(text, DocHygieneTests.InternalReferencePatterns))
            .Select(static departure => LeadingLineNumber.Replace(departure, string.Empty));
    }
}
