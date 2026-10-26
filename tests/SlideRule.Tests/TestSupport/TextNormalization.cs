namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Line-ending normalization for comparisons against text written on another platform — the shell scripts and
///     goldens write LF, the Windows shells and editors that host them write CRLF.
/// </summary>
/// <remarks>
///     Two names, deliberately: under one name, which semantics a comparison gets is an accident of whichever
///     helper is in scope. The name carries the difference instead, so a golden comparison picks trimming or
///     not on purpose, and the reader can see which it picked.
/// </remarks>
internal static class TextNormalization
{
    /// <summary>CRLF folded to LF, leading and trailing whitespace left alone.</summary>
    internal static string NormalizedLines(this string value)
    {
        return value.Replace("\r\n", "\n");
    }

    /// <summary>The file at <paramref name="path" /> read whole, CRLF folded to LF.</summary>
    internal static string ReadNormalizedLines(string path)
    {
        return File.ReadAllText(path)
            .NormalizedLines();
    }

    /// <summary>CRLF folded to LF, then trimmed — for comparisons a trailing newline must not decide.</summary>
    internal static string NormalizedTrimmed(this string value)
    {
        return value.Replace("\r\n", "\n")
            .Trim();
    }

    /// <summary>
    ///     How many times <paramref name="needle" /> occurs in <paramref name="haystack" />, ordinally and
    ///     without overlaps — for the gates that count a marker rather than assert one is present. An empty
    ///     needle throws, since it would match at every index.
    /// </summary>
    internal static int Occurrences(this string haystack, string needle)
    {
        ArgumentException.ThrowIfNullOrEmpty(needle);
        var count = 0;
        for (int index = haystack.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
