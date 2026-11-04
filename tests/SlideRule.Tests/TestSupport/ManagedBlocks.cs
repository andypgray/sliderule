using Shouldly;
using SlideRule.Rendering;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The one-block assertion over a committed file, for the gates that hold one managed block per file.
/// </summary>
internal static class ManagedBlocks
{
    /// <summary>
    ///     Asserts <paramref name="text" /> carries exactly one managed block, its marker text appearing
    ///     nowhere else, and returns the block's body as <see cref="ManagedBlock.ExtractBody" /> reads it.
    /// </summary>
    internal static string ShouldCarryOneManagedBlock(this string text, string customMessage)
    {
        MarkerPairCount(text)
            .ShouldBe(1, customMessage);

        return ManagedBlock.ExtractBody(text)
            .ShouldNotBeNull(customMessage);
    }

    /// <summary>
    ///     Asserts <paramref name="text" /> carries one managed block holding a codebase-survey fence, and returns
    ///     that fence from its opening line through its closing one, with no newline after it. A committed file
    ///     may be checked out with CRLF, so its line endings are read as LF.
    /// </summary>
    internal static string ShouldCarrySurveyFence(this string text, string customMessage)
    {
        string body = text.ShouldCarryOneManagedBlock(customMessage)
            .ReplaceLineEndings("\n");

        int open = body.IndexOf("```mermaid\nflowchart LR\n    accTitle: Codebase survey:", StringComparison.Ordinal);
        open.ShouldBeGreaterThanOrEqualTo(0, $"{customMessage} The managed block holds no survey fence:\n{body}");

        int close = body.IndexOf("\n```", open, StringComparison.Ordinal);
        close.ShouldBeGreaterThan(open, $"{customMessage} The survey fence is never closed:\n{body}");

        return body[open..(close + "\n```".Length)];
    }

    /// <summary>
    ///     How many well-formed marker pairs <paramref name="text" /> carries: the begin-marker count when it
    ///     equals the end-marker count, and -1 when the two disagree, so a <c>ShouldBe(1)</c> reads as "exactly
    ///     one pair" and a lone marker never passes.
    /// </summary>
    private static int MarkerPairCount(string text)
    {
        int begins = text.Occurrences(ManagedBlock.BeginMarker);
        int ends = text.Occurrences(ManagedBlock.EndMarker);
        return begins == ends ? begins : -1;
    }
}
