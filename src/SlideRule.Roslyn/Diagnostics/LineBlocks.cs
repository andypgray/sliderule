namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     Writes a composed multi-line block to a writer, one <c>WriteLine</c> per line.
/// </summary>
/// <remarks>
///     A composed block is joined with <c>\n</c> so that it is one comparable value whatever machine built it.
///     Written whole, those LFs would reach a CRLF console verbatim; written a line at a time, the block adopts
///     the writer's own newline.
/// </remarks>
internal static class LineBlocks
{
    internal static void Write(TextWriter writer, string block)
    {
        foreach (string line in block.Split('\n')) writer.WriteLine(line);
    }

    /// <summary>
    ///     Writes <paramref name="stamp" /> as <see cref="Write" /> does, then the blank line that separates a stamp
    ///     from the answer it scopes. Every stamp goes through here, so no verb can forget the separator.
    /// </summary>
    internal static void WriteStamp(TextWriter writer, string stamp)
    {
        Write(writer, stamp);
        writer.WriteLine();
    }
}
