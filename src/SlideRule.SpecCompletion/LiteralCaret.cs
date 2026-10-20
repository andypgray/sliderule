using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SlideRule.SpecCompletion;

/// <summary>
///     Where the caret sits inside a string literal: whether it is in one at all, and which dotted segment
///     of one it is in.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="InsideLiteral" /> runs before anything is parsed, on one line of raw text, because the
///         host asks whether a keystroke is worth a completion request at all and cannot afford a tree for the
///         answer. Everything after that reads the parsed token, where the kind alone rules out an
///         interpolated, raw or u8 literal that a text scan would have to spell out. The scan is therefore
///         allowed to be the wider of the two: a keystroke it lets through costs one request that finds
///         nothing.
///     </para>
///     <para>
///         Every span this returns is in source coordinates. A regular literal carrying a backslash and a
///         verbatim one carrying a quote are both refused, because in either the content text and the source
///         text stop being the same length and a segment span computed from one would land in the wrong place
///         in the other. Neither shape can spell a namespace or a type name, so refusing them costs an author
///         nothing.
///     </para>
/// </remarks>
internal static class LiteralCaret
{
    /// <summary>
    ///     Whether the caret is inside a string literal, decided from the text of its own line;
    ///     <paramref name="caret" /> is just past the character that was typed.
    /// </summary>
    /// <remarks>
    ///     A literal that opened on an earlier line is not found: only a verbatim literal can span lines, and a
    ///     namespace written across two lines is not a shape to design for. A <c>//</c> reached outside a
    ///     literal ends the scan, so a quote in a comment opens nothing.
    /// </remarks>
    internal static bool InsideLiteral(SourceText text, int caret)
    {
        if (caret <= 0 || caret > text.Length) return false;

        TextLine line = text.Lines.GetLineFromPosition(caret);
        var state = ScanState.Outside;

        int index = line.Start;
        while (index < caret)
        {
            char character = text[index];
            bool pairFits = index + 1 < caret;

            switch (state)
            {
                case ScanState.Outside:
                    if (character == '/' && pairFits && text[index + 1] == '/') return false;

                    if (character == '@' && pairFits && text[index + 1] == '"')
                    {
                        state = ScanState.Verbatim;
                        index += 2;
                        continue;
                    }

                    if (character == '"') state = ScanState.Regular;
                    index++;
                    continue;

                case ScanState.Regular:
                    if (character == '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (character == '"') state = ScanState.Outside;
                    index++;
                    continue;

                default:
                    if (character == '"' && pairFits && text[index + 1] == '"')
                    {
                        index += 2;
                        continue;
                    }

                    if (character == '"') state = ScanState.Outside;
                    index++;
                    continue;
            }
        }

        return state != ScanState.Outside;
    }

    /// <summary>
    ///     The segment of <paramref name="token" /> the caret sits in, or null where the caret is outside
    ///     the literal's content or the literal is one whose spans cannot be computed.
    /// </summary>
    /// <remarks>
    ///     The segment runs from just after the last <c>.</c> before the caret to the next <c>.</c> at or
    ///     after it, or to the end of the content. An unterminated literal has no closing quote to
    ///     subtract, which is the shape a caret is in the moment the opening quote is typed.
    /// </remarks>
    internal static LiteralSegment? SegmentAt(SyntaxToken token, int caret)
    {
        string text = token.Text;
        int start = token.SpanStart;

        bool verbatim = text.Length > 0 && text[0] == '@';
        int opening = verbatim ? 2 : 1;
        if (text.Length < opening) return null;

        bool closed = text.Length > opening && text[text.Length - 1] == '"';
        int contentStart = opening;
        int contentEnd = closed ? text.Length - 1 : text.Length;

        int caretIndex = caret - start;
        if (caretIndex < contentStart || caretIndex > contentEnd) return null;

        for (int index = contentStart; index < contentEnd; index++)
        {
            char character = text[index];
            if (character == (verbatim ? '"' : '\\')) return null;
        }

        int segmentStart = contentStart;
        for (int index = caretIndex - 1; index >= contentStart; index--)
        {
            if (text[index] != '.') continue;

            segmentStart = index + 1;
            break;
        }

        int segmentEnd = contentEnd;
        for (int index = caretIndex; index < contentEnd; index++)
        {
            if (text[index] != '.') continue;

            segmentEnd = index;
            break;
        }

        string path = segmentStart > contentStart
            ? text.Substring(contentStart, segmentStart - contentStart - 1)
            : string.Empty;

        return new LiteralSegment(path, TextSpan.FromBounds(start + segmentStart, start + segmentEnd));
    }

    // Which kind of literal the line scan is inside. Verbatim is parted from regular because the two
    // spell an escape differently, and a scan that read one as the other would leave a literal open.
    private enum ScanState
    {
        Outside,
        Regular,
        Verbatim
    }
}

/// <summary>
///     One dotted segment of a literal: what was written before it, and where it sits in the document.
/// </summary>
internal readonly struct LiteralSegment(string path, TextSpan span)
{
    /// <summary>The content before this segment, with no trailing dot; empty at the literal's first segment.</summary>
    internal string Path { get; } = path;

    /// <summary>The segment's own span, which is what a committed item replaces.</summary>
    internal TextSpan Span { get; } = span;
}
