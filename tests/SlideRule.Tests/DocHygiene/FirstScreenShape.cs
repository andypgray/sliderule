using System.Text.RegularExpressions;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The shape checker the landing page's first screen is held to. The first screen is the fold: every
///     line above the page's first <c>##</c> heading, which is all a skimmer reads. It is plain
///     declaratives when no sentence runs past twenty-five words, no sentence spends an em-dash, and at
///     most one colon sits anywhere but the end of a line — a line-final colon introduces the fence or
///     the list under it, while a colon inside a sentence is the expansion that makes a page read as
///     machine-written. All of the measuring lives here so the gate and its negative tests exercise the
///     same code path.
/// </summary>
/// <remarks>
///     <para>
///         Each prose line is measured on its own, which is how this page is written: one paragraph per
///         line, wrapped by the reader rather than by the author. A hand-wrapped paragraph would be read
///         as several short sentences instead of one long one, so that shape under-reports rather than
///         misreports.
///     </para>
///     <para>
///         Fences come out through <see cref="SourceAnchors.ProseLines" /> — the numbered form
///         <see cref="DocProse.BlankFences" /> is itself built on — because a finding names the line it
///         sits on, and that form hands over each kept line with its number. The fold is found in
///         that list rather than in the raw document, so a <c>##</c> line inside a quoted block cannot
///         end the first screen early.
///     </para>
/// </remarks>
internal static class FirstScreenShape
{
    private const int WordBudget = 25;

    /// <summary>How many colons the fold may spend anywhere but at the end of a line.</summary>
    private const int ColonBudget = 1;

    /// <summary>What an inline code span stands as: one word, carrying no sentence-ending punctuation.</summary>
    private const string CodeSpanToken = "code";

    /// <summary>
    ///     The fold's furniture, which is not prose a reader reads as sentences: the page title, a badge
    ///     row, and a table row. An HTML comment is furniture too and is not here, because it can run past
    ///     the line it opens on — <see cref="FirstScreen" /> skips it by its delimiters instead.
    /// </summary>
    private static readonly string[] NonProsePrefixes = ["# ", "[![", "|"];

    private static readonly Regex CodeSpan = new("`[^`]*`");

    private static readonly Regex ListMarker = new(@"^\s*(?:[0-9]+[.)]|[-*+])\s+");

    private static readonly Regex BlockquoteMarker = new(@"^\s*(?:>\s*)+");

    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+");

    /// <summary>
    ///     Every way the first screen of <paramref name="markdown" /> departs from plain declaratives,
    ///     one finding per offending sentence: <c>"L{line}: {n} words: {sentence}"</c>,
    ///     <c>"L{line}: em-dash: {sentence}"</c>, and — once the fold has spent more than one colon away
    ///     from the end of a line — <c>"L{line}: colon: {sentence}"</c> for each sentence holding one.
    ///     Line numbers are 1-based over the document as given, so a finding names the line to edit.
    /// </summary>
    public static IReadOnlyList<string> Findings(string markdown)
    {
        (int Line, string Sentence, int Colons)[] sentences = FirstScreen(markdown)
            .SelectMany(static line => Sentences(line.Text)
                .Select(sentence => (Line: line.Number, Sentence: sentence, Colons: CountedColons(sentence))))
            .ToArray();

        List<string> findings = new();

        foreach ((int line, string sentence, int _) in sentences)
        {
            int words = DocProse.CountWords(sentence);
            if (words > WordBudget) findings.Add($"L{line}: {words} words: {sentence}");

            if (DocProse.CountEmDashes(sentence) > 0) findings.Add($"L{line}: em-dash: {sentence}");
        }

        int colons = sentences.Sum(static entry => entry.Colons);
        if (colons <= ColonBudget) return findings;

        IEnumerable<string> expansions = sentences
            .Where(static entry => entry.Colons > 0)
            .Select(static entry => $"L{entry.Line}: colon: {entry.Sentence}");
        findings.AddRange(expansions);

        return findings;
    }

    /// <summary>
    ///     The sentences of one prose line, each normalized for measurement: the blockquote marker, the
    ///     list marker and the bold markers gone, an inline code span standing as one word, and a link
    ///     standing as its text. A sentence ends at <c>.</c>, <c>!</c> or <c>?</c> followed by whitespace
    ///     or the end of the line, which is what keeps <c>.NET</c>, <c>1.0</c>, <c>AGENTS.md</c> and a
    ///     dotted URL mid-sentence.
    /// </summary>
    public static IReadOnlyList<string> Sentences(string line)
    {
        string normalized = Normalize(line);

        return SentenceBreak.Split(normalized)
            .Select(static sentence => sentence.Trim())
            .Where(static sentence => sentence.Length > 0)
            .ToArray();
    }

    // An HTML comment is the one piece of furniture that outlives its own line, and a scanner that read it
    // by prefix alone would measure every line of one but the first as a sentence. So the fold is walked
    // with the comment carried across lines: from the line that opens one to the line that closes it,
    // nothing is prose, and a comment opened and closed on one line costs no state at all.
    private static IEnumerable<(string Text, int Number)> FirstScreen(string markdown)
    {
        IEnumerable<(string Text, int Number)> fold = SourceAnchors.ProseLines(markdown)
            .TakeWhile(static line => !line.Text.StartsWith("## ", StringComparison.Ordinal));

        var insideComment = false;

        foreach ((string text, int number) in fold)
        {
            string trimmed = text.Trim();
            bool opensComment = !insideComment && trimmed.StartsWith("<!--", StringComparison.Ordinal);

            if (insideComment || opensComment)
            {
                insideComment = !trimmed.Contains("-->", StringComparison.Ordinal);
                continue;
            }

            if (IsProse(trimmed)) yield return (text, number);
        }
    }

    private static bool IsProse(string trimmedLine)
    {
        if (trimmedLine.Length == 0) return false;

        return !NonProsePrefixes.Any(prefix => trimmedLine.StartsWith(prefix, StringComparison.Ordinal));
    }

    // The blockquote marker comes off before the list marker, because a quoted list carries both, and a
    // quotation is held to the same shape as the prose around it: it is read as sentences, so stripping the
    // marker measures it rather than skipping it.
    private static string Normalize(string line)
    {
        string withoutQuote = BlockquoteMarker.Replace(line, string.Empty);
        string withoutMarker = ListMarker.Replace(withoutQuote, string.Empty);
        string withoutCode = CodeSpan.Replace(withoutMarker, CodeSpanToken);
        string withoutLinks = DocProse.LinksAsText(withoutCode);

        return withoutLinks.Replace("**", string.Empty);
    }

    // Only the last sentence on a line can end in a colon: every earlier one ends at the punctuation the
    // line was split on. So a trailing colon here is the line-final colon, the one that introduces what
    // comes next rather than expanding what came before.
    private static int CountedColons(string sentence)
    {
        int colons = sentence.Count(static character => character == ':');

        return sentence.EndsWith(':') ? colons - 1 : colons;
    }
}
