namespace SlideRule.Internal;

/// <summary>A point in a text <see cref="StrictJson" /> read: where a value, a key or a refusal sits.</summary>
internal readonly struct JsonPosition(int line, int column, int offset)
{
    /// <summary>The 1-based line. A line ends at a line feed; a carriage return does not end one.</summary>
    internal int Line { get; } = line;

    /// <summary>
    ///     The 1-based column, counted in UTF-16 code units. A carriage return takes a column like any other
    ///     character, so a CRLF file reports the positions its line-feed-only twin would.
    /// </summary>
    internal int Column { get; } = column;

    /// <summary>The 0-based index into the text that was read.</summary>
    internal int Offset { get; } = offset;

    /// <summary><paramref name="text" /> with this position in front: <c>line 2, column 8: ...</c>.</summary>
    internal string Describe(string text)
    {
        string line = JsonText.Digits(Line);
        string column = JsonText.Digits(Column);
        return "line " + line + ", column " + column + ": " + text;
    }
}
