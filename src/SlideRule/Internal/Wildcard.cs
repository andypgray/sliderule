namespace SlideRule.Internal;

/// <summary>
///     The shared iterative <c>*</c>-glob matcher behind both simple-name and namespace-segment
///     matching (GRAMMAR §4.2, §5.2), and behind the package globs (§4.10). <c>*</c> matches any run of
///     characters including the empty run; every other character is an ordinal (case-sensitive) match,
///     or an ordinal case-insensitive one for a caller that asks, as the package verbs do because NuGet
///     compares package identifiers that way. It has no notion of a dot separator — dot-crossing is
///     decided by whoever tokenizes the input before calling in.
/// </summary>
internal static class Wildcard
{
    internal static bool Match(string pattern, string text)
    {
        return Match(pattern, text, ignoreCase: false);
    }

    internal static bool Match(string pattern, string text, bool ignoreCase)
    {
        // star/mark remember the last '*' seen and how far the text had advanced when it was taken, so a
        // dead end backtracks by letting that '*' swallow one more character instead of rescanning from
        // the start; star = -1 means no '*' is available to backtrack to, so the mismatch is final.
        var p = 0;
        var t = 0;
        int star = -1;
        var mark = 0;

        while (t < text.Length)
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (p < pattern.Length && SameCharacter(pattern[p], text[t], ignoreCase))
            {
                p++;
                t++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }

        while (p < pattern.Length && pattern[p] == '*') p++;

        return p == pattern.Length;
    }

    /// <summary>Whether any of <paramref name="patterns" /> matches <paramref name="text" />.</summary>
    internal static bool MatchesAny(IReadOnlyList<string> patterns, string text)
    {
        return MatchesAny(patterns, text, ignoreCase: false);
    }

    /// <summary>
    ///     Whether any of <paramref name="patterns" /> matches <paramref name="text" />, ignoring case where
    ///     <paramref name="ignoreCase" /> says so.
    /// </summary>
    internal static bool MatchesAny(IReadOnlyList<string> patterns, string text, bool ignoreCase)
    {
        // An index walk rather than a LINQ Any, because it runs once per candidate name — a project, an edge end,
        // a rule ID — over a list that is usually a single glob.
        for (var i = 0; i < patterns.Count; i++)
            if (Match(patterns[i], text, ignoreCase))
                return true;

        return false;
    }

    // Upper-casing both sides invariantly, one character at a time, is the comparison
    // StringComparison.OrdinalIgnoreCase makes, which is the one NuGet compares package identifiers with.
    private static bool SameCharacter(char patternChar, char textChar, bool ignoreCase)
    {
        if (patternChar == textChar) return true;

        return ignoreCase && char.ToUpperInvariant(patternChar) == char.ToUpperInvariant(textChar);
    }
}
