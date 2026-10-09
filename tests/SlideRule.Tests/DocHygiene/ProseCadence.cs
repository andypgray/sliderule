using System.Text;
using System.Text.RegularExpressions;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The cadence checker a reader doc is held to across its whole body: how many of its sentences run past
///     twenty-five words, how many carry a colon before their last word, and how it spends its contrast
///     frames. A doc keeps its cadence when at most one sentence in five runs long, at most one in ten
///     carries a counted colon, no contrast frame appears twice, and no section holds two. All of the
///     measuring lives here so the gates and their negative tests exercise the same code path.
/// </summary>
/// <remarks>
///     <para>
///         Fenced blocks and HTML comments come out, and so does every line that opens with a heading,
///         table, HTML or image mark. A code span stands as the single capital <c>C</c>, and a link as its
///         bracketed text. Paragraphs split at blank lines, each line of a paragraph loses one list or quote
///         marker, and whitespace runs collapse. A sentence ends at <c>.</c>, <c>!</c> or <c>?</c> followed
///         by whitespace and then a capital letter, a double quote, an opening parenthesis or an asterisk,
///         and a sentence of fewer than three words is not counted. A sentence that opens with a link
///         therefore runs on from the one before it, and so does a list item with no end punctuation; a
///         blank line between list items keeps them apart.
///     </para>
///     <para>
///         Two kinds of colon are counted out. A colon that ends its line when the next line opens a list
///         item introduces that list. The colon after a list item's leading name (a bold run, a code span or
///         a link, with an optional parenthetical) pairs the name with its description.
///     </para>
///     <para>
///         A contrast frame is <c>rather than</c>, <c>not just</c>, <c>not only</c>, <c>not merely</c>, or a
///         staged negation: <c>isn't</c>, <c>is not</c>, <c>aren't</c> or <c>are not</c>, then within sixty
///         characters a comma, a semicolon, or a period and whitespace, then <c>it's</c>, <c>it is</c>,
///         <c>they're</c>, <c>they are</c> or <c>but</c>. Each phrase is a kind of its own, and every staged
///         negation is one kind however its halves are worded. A section runs from a heading of any level to
///         the next, and the text above the first heading is a section too.
///     </para>
///     <para>
///         <see cref="FirstScreenShape" /> holds the landing page's first screen line by line and more
///         strictly; neither checker calls the other, so neither gate moves when the other changes.
///     </para>
/// </remarks>
internal static class ProseCadence
{
    /// <summary>A sentence of more words than this runs long.</summary>
    private const int WordBudget = 25;

    /// <summary>At most one sentence in this many may run long.</summary>
    private const int OneLongSentenceIn = 5;

    /// <summary>At most one sentence in this many may carry a counted colon.</summary>
    private const int OneColonSentenceIn = 10;

    /// <summary>A sentence of fewer words than this is not counted.</summary>
    private const int FewestWords = 3;

    /// <summary>
    ///     What a code span stands as while measured: one word and a capital, so a sentence that opens with
    ///     one starts a new sentence, and the frame pattern's sixty-character reach counts it as one character.
    /// </summary>
    private const char CodeSpanToken = 'C';

    /// <summary>The kind every staged negation is, however its halves are worded.</summary>
    private const string StagedFrame = "isn't X, it's Y";

    /// <summary>A line that opens with a heading, table, HTML or image mark.</summary>
    private static readonly Regex FurnitureLine = new(@"^\s*(?:#|\||<|!\[).*$", RegexOptions.Multiline);

    /// <summary>An inline code span, which never crosses a line.</summary>
    private static readonly Regex CodeSpan = new("`[^`\n]*`");

    /// <summary>A link's target, from its closing bracket through its closing parenthesis.</summary>
    private static readonly Regex LinkTarget = new(@"\]\([^)]*\)");

    /// <summary>One or more blank lines between paragraphs.</summary>
    private static readonly Regex ParagraphBreak = new(@"\n\s*\n");

    /// <summary>One list or quote marker at the start of a line.</summary>
    private static readonly Regex LineMarker = new(@"^\s*(?:[-*+>]|\d+\.)\s+", RegexOptions.Multiline);

    /// <summary>A whitespace run, line breaks included.</summary>
    private static readonly Regex WhitespaceRun = new(@"\s+");

    /// <summary>Where one sentence ends and the next begins.</summary>
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+(?=[A-Z""(*])");

    /// <summary>A colon with more of its sentence after it.</summary>
    private static readonly Regex MidSentenceColon = new(@":(?=\s+\S)");

    /// <summary>A colon ending its line when the next line opens a list item.</summary>
    private static readonly Regex ListIntroColon = new(@":(?=[ \t]*\n[ \t]*(?:[-*+]|\d+\.)[ \t])");

    /// <summary>A list item's leading name, optional parenthetical, and the colon pairing it with its description.</summary>
    private static readonly Regex NameColon = new(
        @"^[ \t]*(?:[-*+]|\d+\.)[ \t]+(?:\*\*[^*\n]+\*\*|`[^`\n]+`|\[[^\]\n]+\]\([^)\n]*\))(?:[ \t]*\([^)\n]*\))?(?<colon>:)",
        RegexOptions.Multiline);

    /// <summary>A contrast frame; <c>phrase</c> holds a fixed phrase and stays empty for a staged negation.</summary>
    private static readonly Regex Frame = new(
        @"\b(?<phrase>rather than|not (?:just|only|merely))\b"
        + @"|\b(?:isn.t|is not|aren.t|are not)\b[^.;:\n]{1,60}(?:[,;]|\.\s+)\s*(?:it.s|it is|they.re|they are|but)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Every counted sentence of <paramref name="markdown" />'s prose, in document order.</summary>
    public static IReadOnlyList<MeasuredSentence> Sentences(string markdown)
    {
        Reading reading = Reading.Of(markdown);

        return reading.Prose.Split(ParagraphBreak)
            .Select(static paragraph => paragraph
                .Replace(LineMarker, string.Empty)
                .Replace(WhitespaceRun, " "))
            .SelectMany(static paragraph => paragraph.Split(SentenceBreak))
            .Where(static sentence => DocProse.CountWords(sentence.Text) >= FewestWords)
            .Select(reading.Measure)
            .ToArray();
    }

    /// <summary>
    ///     Every way <paramref name="markdown" /> runs past its sentence budgets. When more than one sentence
    ///     in five runs past twenty-five words, a count line
    ///     <c>"{n} of {total} sentences run past 25 words (at most {budget} may)"</c> is followed by each of
    ///     them as <c>"L{line}: {words} words: {sentence}"</c>; when more than one in ten carries a counted
    ///     colon, <c>"{n} of {total} sentences carry a colon mid-sentence (at most {budget} may)"</c> is
    ///     followed by each as <c>"L{line}: colon: {sentence}"</c>. A budget that holds reports nothing.
    /// </summary>
    public static IReadOnlyList<string> Findings(string markdown)
    {
        IReadOnlyList<MeasuredSentence> sentences = Sentences(markdown);
        MeasuredSentence[] longOnes = sentences
            .Where(static sentence => sentence.Words > WordBudget)
            .ToArray();
        MeasuredSentence[] colonOnes = sentences
            .Where(static sentence => sentence.HasCountedColon)
            .ToArray();
        List<string> findings = new();

        if (longOnes.Length * OneLongSentenceIn > sentences.Count)
        {
            findings.Add($"{longOnes.Length} of {sentences.Count} sentences run past {WordBudget} words "
                         + $"(at most {sentences.Count / OneLongSentenceIn} may)");
            findings.AddRange(longOnes.Select(static sentence =>
                $"L{sentence.Line}: {sentence.Words} words: {sentence.Text}"));
        }

        if (colonOnes.Length * OneColonSentenceIn > sentences.Count)
        {
            findings.Add($"{colonOnes.Length} of {sentences.Count} sentences carry a colon mid-sentence "
                         + $"(at most {sentences.Count / OneColonSentenceIn} may)");
            findings.AddRange(colonOnes.Select(static sentence => $"L{sentence.Line}: colon: {sentence.Text}"));
        }

        return findings;
    }

    /// <summary>Every contrast frame in <paramref name="markdown" />'s prose, in document order.</summary>
    public static IReadOnlyList<ContrastFrame> Frames(string markdown)
    {
        Reading reading = Reading.Of(markdown);
        IReadOnlyList<(string Text, int Number)> headings = DocProse.HeadingLines(markdown);

        return Frame.Matches(reading.Prose.Text)
            .Select(match => reading.Describe(match, headings))
            .ToArray();
    }

    /// <summary>
    ///     Every way <paramref name="markdown" /> spends a contrast frame twice: each use of a kind that
    ///     appears more than once, as <c>"L{line}: \"{kind}\", {n} times in the doc: {frame}"</c>, then each
    ///     frame in a section holding more than one, as <c>"L{line}: {n} frames under \"{heading}\": {frame}"</c>
    ///     (<c>"above the first heading"</c> for the text before any heading).
    /// </summary>
    public static IReadOnlyList<string> FrameFindings(string markdown)
    {
        IReadOnlyList<ContrastFrame> frames = Frames(markdown);

        IEnumerable<string> repeated = frames
            .GroupBy(static frame => frame.Kind)
            .Where(static uses => uses.Count() > 1)
            .SelectMany(static uses => uses.Select(frame =>
                $"L{frame.Line}: \"{frame.Kind}\", {uses.Count()} times in the doc: {frame.Text}"));
        IEnumerable<string> crowded = frames
            .GroupBy(static frame => frame.SectionLine)
            .Where(static section => section.Count() > 1)
            .SelectMany(static section => section.Select(frame =>
                $"L{frame.Line}: {section.Count()} frames {Placement(frame)}: {frame.Text}"));

        return repeated.Concat(crowded)
            .ToArray();
    }

    private static string Placement(ContrastFrame frame)
    {
        return frame.SectionLine == 0 ? "above the first heading" : $"under \"{frame.Section}\"";
    }

    /// <summary>
    ///     One counted sentence: the 1-based <paramref name="Line" /> its first word sits on, its
    ///     <paramref name="Words" />, whether it carries a colon the budget counts, and its
    ///     <paramref name="Text" /> as measured, with each code span shown as written.
    /// </summary>
    public sealed record MeasuredSentence(int Line, int Words, bool HasCountedColon, string Text);

    /// <summary>
    ///     One contrast frame: the 1-based <paramref name="Line" /> it starts on, its <paramref name="Kind" />,
    ///     the line and text of the heading whose section holds it (<c>0</c> and empty above the first
    ///     heading), and its words as written.
    /// </summary>
    public sealed record ContrastFrame(int Line, string Kind, int SectionLine, string Section, string Text);

    /// <summary>
    ///     One doc made ready to measure: its text with fenced lines emptied, where each line starts, the
    ///     offsets of the counted-out colons, and its prose after the rewrites that apply to the whole doc.
    /// </summary>
    private sealed class Reading
    {
        private readonly HashSet<int> _countedOut;
        private readonly string _document;
        private readonly int[] _lineStarts;

        private Reading(string document, HashSet<int> countedOut, TracedText prose)
        {
            _document = document;
            _countedOut = countedOut;
            _lineStarts = LineStarts(document);
            Prose = prose;
        }

        /// <summary>The prose after the doc-wide rewrites, before it splits into paragraphs.</summary>
        public TracedText Prose { get; }

        /// <summary>Reads <paramref name="markdown" /> for measuring.</summary>
        public static Reading Of(string markdown)
        {
            string document = DocProse.BlankFences(markdown);
            IEnumerable<int> listIntros = ListIntroColon.Matches(document)
                .Select(static match => match.Index);
            IEnumerable<int> names = NameColon.Matches(document)
                .Select(static match => match.Groups["colon"].Index);
            HashSet<int> countedOut = [.. listIntros, .. names];
            TracedText prose = TracedText.Of(document)
                .Replace(DocProse.HtmlComment, string.Empty)
                .Replace(FurnitureLine, string.Empty)
                .Replace(CodeSpan, CodeSpanToken.ToString())
                .Replace(LinkTarget, "]");

            return new Reading(document, countedOut, prose);
        }

        /// <summary>Measures one sentence: its words, its counted colon, and the line of its first word.</summary>
        public MeasuredSentence Measure(TracedText sentence)
        {
            int words = DocProse.CountWords(sentence.Text);
            bool countedColon = MidSentenceColon.Matches(sentence.Text)
                .Any(match => !_countedOut.Contains(sentence.Origin(match.Index)));
            int firstWord = sentence.Text.Length - sentence.Text.TrimStart().Length;
            int line = LineAt(sentence.Origin(firstWord));

            return new MeasuredSentence(line, words, countedColon, Render(sentence));
        }

        /// <summary>Describes one frame <paramref name="match" /> in <see cref="Prose" /> and its section.</summary>
        public ContrastFrame Describe(Match match, IReadOnlyList<(string Text, int Number)> headings)
        {
            TracedText frame = Prose.Slice(match.Index, match.Index + match.Length);
            int line = LineAt(frame.Origin(0));
            Group phrase = match.Groups["phrase"];
            string kind = phrase.Success ? phrase.Value.ToLowerInvariant() : StagedFrame;
            (string Text, int Number) section =
                headings.LastOrDefault(heading => heading.Number < line, (Text: string.Empty, Number: 0));
            string text = WhitespaceRun.Replace(Render(frame), " ");

            return new ContrastFrame(line, kind, section.Number, section.Text.Trim(), text);
        }

        private static int[] LineStarts(string document)
        {
            IEnumerable<int> afterBreaks = document
                .Select(static (character, index) => character == '\n' ? index + 1 : -1)
                .Where(static start => start > 0);

            return [0, .. afterBreaks];
        }

        // BinarySearch answers an exact hit's index or the complement of the first larger entry, which over
        // the line starts is the 1-based line either way.
        private int LineAt(int offset)
        {
            int found = Array.BinarySearch(_lineStarts, offset);
            return found >= 0 ? found + 1 : ~found;
        }

        // A code span is measured as the single capital C, which a reader cannot find in the doc, so a
        // finding shows each span as written. Only a stand-in has a backtick at its origin.
        private string Render(TracedText piece)
        {
            StringBuilder rendered = new(piece.Text.Length);
            for (var index = 0; index < piece.Text.Length; index++)
            {
                int origin = piece.Origin(index);
                bool standIn = piece.Text[index] == CodeSpanToken && _document[origin] == '`';
                if (!standIn)
                {
                    rendered.Append(piece.Text[index]);
                    continue;
                }

                int closing = _document.IndexOf('`', origin + 1);
                rendered.Append(_document, origin, closing - origin + 1);
            }

            return rendered.ToString()
                .Trim();
        }
    }

    /// <summary>
    ///     Text being measured, each character paired with the offset in the fence-emptied doc it came from,
    ///     so the line a sentence starts on and the counted-out colons survive every rewrite.
    /// </summary>
    private sealed class TracedText(string text, int[] origins)
    {
        /// <summary>The text as the measure now reads it.</summary>
        public string Text { get; } = text;

        /// <summary>Starts tracing <paramref name="document" />: every character is its own origin.</summary>
        public static TracedText Of(string document)
        {
            int[] origins = Enumerable.Range(0, document.Length)
                .ToArray();
            return new TracedText(document, origins);
        }

        /// <summary>The doc offset the character at <paramref name="index" /> came from.</summary>
        public int Origin(int index)
        {
            return origins[index];
        }

        /// <summary>
        ///     Every match of <paramref name="pattern" /> replaced; each replacement character takes the offset
        ///     its match started at, and every other character keeps its own. No pattern here matches empty text.
        /// </summary>
        public TracedText Replace(Regex pattern, string replacement)
        {
            StringBuilder rewritten = new(Text.Length);
            List<int> rewrittenOrigins = new(Text.Length);
            var kept = 0;

            foreach (Match match in pattern.Matches(Text))
            {
                rewritten.Append(Text, kept, match.Index - kept);
                rewrittenOrigins.AddRange(origins[kept..match.Index]);
                rewritten.Append(replacement);
                rewrittenOrigins.AddRange(Enumerable.Repeat(origins[match.Index], replacement.Length));
                kept = match.Index + match.Length;
            }

            rewritten.Append(Text, kept, Text.Length - kept);
            rewrittenOrigins.AddRange(origins[kept..]);

            return new TracedText(rewritten.ToString(), rewrittenOrigins.ToArray());
        }

        /// <summary>The pieces between matches of <paramref name="separator" />, empty ones included.</summary>
        public IEnumerable<TracedText> Split(Regex separator)
        {
            var from = 0;
            foreach (Match match in separator.Matches(Text))
            {
                yield return Slice(from, match.Index);
                from = match.Index + match.Length;
            }

            yield return Slice(from, Text.Length);
        }

        /// <summary>The characters from <paramref name="start" /> up to <paramref name="end" />, with their origins.</summary>
        public TracedText Slice(int start, int end)
        {
            return new TracedText(Text[start..end], origins[start..end]);
        }
    }
}
