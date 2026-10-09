using System.Text.RegularExpressions;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The markdown reader the documentation gates run on. It blanks fenced code blocks and other
///     matches in place, reads a doc's headings and plain text, and finds references the published
///     documentation is meant to stay free of. The gate tests and the negative tests both call it, so
///     they exercise the same code path.
/// </summary>
internal static class DocProse
{
    private static readonly Regex InlineLink = new(@"\[([^\]]*)\]\([^)]*\)");

    private static readonly Regex WhitespaceRun = new(@"\s+");

    /// <summary>
    ///     An ATX heading's opening: up to three spaces of indent, one to six hashes, then a space, a tab or the
    ///     end of the line.
    /// </summary>
    internal static readonly Regex AtxHeadingOpening = new(@"^ {0,3}#{1,6}(?:[ \t]|$)", RegexOptions.CultureInvariant);

    /// <summary>An HTML comment, which can run across lines.</summary>
    internal static readonly Regex HtmlComment = new(@"<!--[\s\S]*?-->", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Returns <paramref name="text" /> with every line of every fenced code block emptied, delimiter lines
    ///     included, and every other line kept where it was, so a position in the result names the same line
    ///     in the doc. Newlines come back as <c>"\n"</c>.
    /// </summary>
    /// <remarks>
    ///     Where a fence opens and closes is <see cref="SourceAnchors.ProseLines" />'s decision — the quote gates
    ///     and the prose gates read one scanner, or a doc could be fenced for one of them and not the other.
    /// </remarks>
    public static string BlankFences(string text)
    {
        int lineCount = text.NormalizedLines()
            .Split('\n')
            .Length;
        string[] kept = Enumerable.Repeat(string.Empty, lineCount)
            .ToArray();

        foreach ((string line, int number) in SourceAnchors.ProseLines(text))
            kept[number - 1] = line;

        return string.Join("\n", kept);
    }

    /// <summary>
    ///     Returns <paramref name="text" /> with every match of <paramref name="patterns" /> overwritten by spaces,
    ///     newlines kept, so a match that wraps across lines is blanked whole and a position in the result names
    ///     the same line in the doc.
    /// </summary>
    public static string BlankMatches(string text, IEnumerable<Regex> patterns)
    {
        return patterns.Aggregate(
            text,
            static (current, pattern) => pattern.Replace(current, static match => BlankKeepingNewlines(match.Value)));
    }

    private static string BlankKeepingNewlines(string value)
    {
        char[] blanked = value.Select(static character => character == '\n' ? '\n' : ' ')
            .ToArray();

        return new string(blanked);
    }

    /// <summary>
    ///     Every ATX heading line of <paramref name="text" /> outside fenced code blocks, as written, paired with
    ///     its 1-based line number in the doc.
    /// </summary>
    public static IReadOnlyList<(string Text, int Number)> HeadingLines(string text)
    {
        return SourceAnchors.ProseLines(text)
            .Where(static line => AtxHeadingOpening.IsMatch(line.Text))
            .ToArray();
    }

    /// <summary>Returns <paramref name="markdown" /> with each inline link replaced by its text.</summary>
    public static string LinksAsText(string markdown)
    {
        return InlineLink.Replace(markdown, static link => link.Groups[1].Value);
    }

    /// <summary>
    ///     Returns <paramref name="markdown" /> as the words a reader sees: each inline link as its text, each
    ///     code span without its backticks, and every whitespace run, line breaks included, as one space.
    /// </summary>
    /// <remarks>
    ///     This is a hard-wrapped readme sentence as it reads once copied somewhere markdown does not render,
    ///     such as a package description, so a gate can compare the two the way a reader would. Emphasis marks
    ///     stay, because a bare asterisk is also a namespace pattern's wildcard.
    /// </remarks>
    public static string PlainText(string markdown)
    {
        string linksAsText = LinksAsText(markdown);
        string withoutCodeMarks = linksAsText.Replace("`", string.Empty);

        return WhitespaceRun.Replace(withoutCodeMarks, " ")
            .Trim();
    }

    /// <summary>
    ///     Finds every match of <paramref name="patterns" /> in <paramref name="text" />, each formatted
    ///     <c>"{lineNumber}: {matchedText}"</c> with 1-based line numbers over the text as given, so a
    ///     failing gate names every offending line.
    /// </summary>
    public static IReadOnlyList<string> FindForbidden(string text, IEnumerable<Regex> patterns)
    {
        Regex[] patternList = patterns as Regex[] ?? patterns.ToArray();
        string normalized = text.NormalizedLines();

        Regex[] present = patternList.Where(pattern => CanSkipWholeText(pattern) || pattern.IsMatch(normalized))
            .ToArray();
        if (present.Length == 0) return [];

        string[] lines = normalized.Split('\n');
        List<string> hits = new();

        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            foreach (Regex pattern in present)
            foreach (Match match in pattern.Matches(line))
                hits.Add($"{index + 1}: {match.Value}");
        }

        return hits;
    }

    // A pattern that matches somewhere in a line also matches the text those lines came from, so one
    // pass over the whole text is a sound filter for the per-line pass — and the callers that scan a
    // repository run hundreds of patterns over tens of thousands of lines that hit nothing at all.
    // The exception is an anchored pattern: without Multiline, '^' and '$' mean the ends of the text
    // rather than the ends of a line, so those skip the filter and are always located line by line.
    private static bool CanSkipWholeText(Regex pattern)
    {
        var source = pattern.ToString();

        return source.Contains('^') || source.Contains('$');
    }
}
