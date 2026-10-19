using SlideRule.Rendering;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Counts of the managed-block markers in a committed file, for the gates that hold one block per file.
/// </summary>
internal static class ManagedBlocks
{
    /// <summary>
    ///     How many well-formed marker pairs <paramref name="text" /> carries: the begin-marker count when it
    ///     equals the end-marker count, and -1 when the two disagree, so a <c>ShouldBe(1)</c> reads as "exactly
    ///     one pair" and a lone marker never passes.
    /// </summary>
    internal static int MarkerPairCount(string text)
    {
        int begins = text.Occurrences(ManagedBlock.BeginMarker);
        int ends = text.Occurrences(ManagedBlock.EndMarker);
        return begins == ends ? begins : -1;
    }
}
