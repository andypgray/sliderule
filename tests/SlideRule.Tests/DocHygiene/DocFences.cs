using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The fenced-block scanner the doc gates share: a doc's fenced blocks with their info strings and line
///     numbers, its fenced lines, and its prose. A change to how it reads a fence changes what every gate built
///     on it sees, so the gates and the unit tests exercise this one code path.
/// </summary>
internal static class DocFences
{
    /// <summary>
    ///     Splits <paramref name="docText" /> into its fenced code blocks in document order, each with the
    ///     info string its opening delimiter carries, its content lines, every line paired with its 1-based
    ///     line number in the doc, and the line number of the opening delimiter. The info string is the rest
    ///     of the opening line after the backtick or tilde run, with spaces and tabs trimmed both sides, and
    ///     empty for a bare fence; an unclosed final fence keeps its info string. <see cref="Scan" /> decides
    ///     everything else: where a fence opens and closes, and which lines it holds.
    /// </summary>
    public static IReadOnlyList<TaggedBlock> TaggedBlocks(string docText)
    {
        List<TaggedBlock> blocks = new();
        var info = string.Empty;
        var opening = 0;
        var openingText = string.Empty;
        List<(string Text, int Number)>? current = null;

        foreach ((string text, int number, LineKind kind) in Scan(docText))
            if (kind == LineKind.FenceOpen)
            {
                info = InfoString(text);
                opening = number;
                openingText = text;
                current = new List<(string, int)>();
            }
            else if (current is not null && kind == LineKind.FenceClose)
            {
                blocks.Add(new TaggedBlock(info, current, opening, openingText));
                current = null;
            }
            else if (current is not null && kind == LineKind.Fenced)
            {
                current.Add((text, number));
            }

        if (current is not null) blocks.Add(new TaggedBlock(info, current, opening, openingText));

        return blocks;
    }

    /// <summary>
    ///     Splits <paramref name="docText" /> into the content of each fenced code block, one list per
    ///     block in document order, each line paired with its 1-based line number in the doc so a caller
    ///     can name the exact place a quote lives: <see cref="TaggedBlocks" /> with each block reduced to its lines.
    ///     The fence lines themselves are dropped and the lines between them are kept verbatim, and an
    ///     unclosed fence keeps every line to the end of the text as its final block.
    /// </summary>
    /// <remarks>
    ///     A caller needing both block identity and position — a <c>grandfathered: N</c> sub-line takes its
    ///     rule id from the header line above it <em>in the same fence</em>, and must still fail naming
    ///     <c>doc:line</c> — reads this directly.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<(string Text, int Number)>> FencedBlocks(string docText)
    {
        return TaggedBlocks(docText)
            .Select(static block => block.Lines)
            .ToArray();
    }

    /// <summary>
    ///     Every line of <paramref name="docText" /> that sits inside a fenced code block, paired with its
    ///     1-based line number in the doc — <see cref="FencedBlocks" /> flattened, for the callers that
    ///     want positions and do not care which block a line came from.
    /// </summary>
    public static IReadOnlyList<(string Text, int Number)> FencedLines(string docText)
    {
        return FencedBlocks(docText)
            .SelectMany(static block => block)
            .ToArray();
    }

    /// <summary>
    ///     The content lines of each fenced code block of <paramref name="docText" />, one list per block
    ///     in document order — <see cref="FencedBlocks" /> without the line numbers, for the callers that
    ///     hold a quoted block to the source it was cut from and locate it by a marker rather than by
    ///     position.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Fences(string docText)
    {
        return FencedBlocks(docText)
            .Select(static block => (IReadOnlyList<string>)block
                .Select(static line => line.Text)
                .ToArray())
            .ToArray();
    }

    /// <summary>
    ///     Every line of <paramref name="docText" /> outside every fenced code block, paired with its
    ///     1-based line number in the doc — the fence delimiter lines and everything between them dropped.
    ///     This is the doc's prose, which the prose-hygiene checks measure.
    /// </summary>
    public static IReadOnlyList<(string Text, int Number)> ProseLines(string docText)
    {
        return Scan(docText)
            .Where(static line => line.Kind == LineKind.Prose)
            .Select(static line => (line.Text, line.Number))
            .ToArray();
    }

    /// <summary>
    ///     Every line of <paramref name="docText" /> that <see cref="FencedLines" /> does not cover: the
    ///     prose <em>and</em> the fence delimiter lines themselves. This is the complement by line number,
    ///     for the sweeps that ask what a doc says where no fenced gate is watching — only the quoted
    ///     content between the delimiters is exempt from those, and an opening delimiter's info string is
    ///     text like any other line.
    /// </summary>
    public static IReadOnlyList<(string Text, int Number)> UnfencedLines(string docText)
    {
        return Scan(docText)
            .Where(static line => line.Kind != LineKind.Fenced)
            .Select(static line => (line.Text, line.Number))
            .ToArray();
    }

    /// <summary>What the fence scan made of one line.</summary>
    private enum LineKind
    {
        /// <summary>Outside every fence.</summary>
        Prose,

        /// <summary>The delimiter line that opens a fence.</summary>
        FenceOpen,

        /// <summary>The delimiter line that closes a fence.</summary>
        FenceClose,

        /// <summary>Inside a fence: quoted content.</summary>
        Fenced
    }

    /// <summary>
    ///     Every line of <paramref name="docText" /> in document order, each labelled with what the scan
    ///     made of it. A fence opens on a line whose first non-whitespace content is a run of three or more
    ///     backticks or tildes and closes on the next line with at least as long a run of the same
    ///     character and nothing after it but whitespace; an unclosed fence holds every line to the end of
    ///     the text. Input newlines are normalized to <c>"\n"</c> first, so a <c>\r</c> never reaches a
    ///     caller.
    /// </summary>
    /// <remarks>
    ///     This is the one fence scanner the quote gates share, and the only place the state machine
    ///     lives; every other view of a doc here is a filter over it.
    /// </remarks>
    private static IEnumerable<(string Text, int Number, LineKind Kind)> Scan(string docText)
    {
        string[] lines = docText.NormalizedLines()
            .Split('\n');
        var insideFence = false;
        var fenceChar = '\0';
        var fenceLength = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            int number = index + 1;

            if (!insideFence)
            {
                if (TryOpenFence(line, out fenceChar, out fenceLength))
                {
                    insideFence = true;
                    yield return (line, number, LineKind.FenceOpen);
                }
                else
                {
                    yield return (line, number, LineKind.Prose);
                }
            }
            else if (ClosesFence(line, fenceChar, fenceLength))
            {
                insideFence = false;
                fenceChar = '\0';
                fenceLength = 0;
                yield return (line, number, LineKind.FenceClose);
            }
            else
            {
                yield return (line, number, LineKind.Fenced);
            }
        }
    }

    private static bool TryOpenFence(string line, out char fenceChar, out int fenceLength)
    {
        fenceChar = '\0';
        fenceLength = 0;
        int index = SkipLeadingWhitespace(line);
        if (index >= line.Length) return false;

        char candidate = line[index];
        if (candidate != '`' && candidate != '~') return false;

        int run = RunLength(line, index, candidate);
        if (run < 3) return false;

        fenceChar = candidate;
        fenceLength = run;
        return true;
    }

    private static bool ClosesFence(string line, char fenceChar, int minimumLength)
    {
        int index = SkipLeadingWhitespace(line);
        int run = RunLength(line, index, fenceChar);
        if (run < minimumLength) return false;

        int afterRun = index + run;
        while (afterRun < line.Length)
        {
            if (line[afterRun] != ' ' && line[afterRun] != '\t') return false;

            afterRun++;
        }

        return true;
    }

    // The info string of an opening delimiter: what follows its backtick or tilde run, trimmed of spaces and tabs.
    private static string InfoString(string line)
    {
        int index = SkipLeadingWhitespace(line);
        int run = RunLength(line, index, line[index]);

        return line[(index + run)..]
            .Trim(' ', '\t');
    }

    private static int SkipLeadingWhitespace(string line)
    {
        var index = 0;
        while (index < line.Length && (line[index] == ' ' || line[index] == '\t')) index++;

        return index;
    }

    private static int RunLength(string line, int start, char character)
    {
        int index = start;
        while (index < line.Length && line[index] == character) index++;

        return index - start;
    }

    /// <summary>
    ///     One fenced code block: the <see cref="Info" /> string its opening delimiter carries, and its content
    ///     <see cref="Lines" />, each paired with its 1-based line number in the doc. <see cref="OpeningLine" /> is
    ///     the 1-based line of the opening delimiter, which is the only place an empty block can be named.
    ///     <see cref="Opening" /> is that delimiter line as written, its newline normalized.
    /// </summary>
    public sealed record TaggedBlock(
        string Info,
        IReadOnlyList<(string Text, int Number)> Lines,
        int OpeningLine,
        string Opening);
}
