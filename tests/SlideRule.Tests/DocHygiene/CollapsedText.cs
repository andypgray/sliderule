using System.Collections.Concurrent;
using System.Text;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     A doc's text with its whitespace collapsed, keeping the doc line each character came from so a
///     fragment found by containment can still be located by line.
/// </summary>
internal sealed class CollapsedText(string text, IReadOnlyList<int> lines)
{
    // One collapse per doc rather than one per claim: several docs carry more than one registered sentence, and
    // collapsing walks the whole file.
    private static readonly ConcurrentDictionary<string, CollapsedText> Collapsed = new(StringComparer.Ordinal);

    /// <summary>The collapsed text: no line breaks, every whitespace run one space.</summary>
    public string Text { get; } = text;

    /// <summary><paramref name="doc" />'s committed text collapsed, read and collapsed once per run.</summary>
    internal static CollapsedText Of(string doc)
    {
        return Collapsed.GetOrAdd(doc, static path => From(RepoRoot.ReadText(path)));
    }

    /// <summary>
    ///     Collapses <paramref name="docText" />'s whitespace runs to single spaces, keeping the doc line
    ///     each surviving character came from. Prose wraps, so a registered fragment routinely spans a
    ///     line break; collapsing is what lets containment find it, and the retained line numbers are
    ///     what let a match still be reported — and claimed — by position.
    /// </summary>
    internal static CollapsedText From(string docText)
    {
        string normalized = docText.NormalizedLines();
        StringBuilder builder = new(normalized.Length);
        List<int> lines = new(normalized.Length);
        var line = 1;
        var pendingSpace = false;

        foreach (char character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                if (character == '\n') line++;
                pendingSpace |= builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                lines.Add(line);
                pendingSpace = false;
            }

            builder.Append(character);
            lines.Add(line);
        }

        return new CollapsedText(builder.ToString(), lines);
    }

    /// <summary>The 1-based doc line the character at <paramref name="index" /> came from.</summary>
    public int LineAt(int index)
    {
        return lines[index];
    }

    /// <summary>
    ///     The inclusive doc line range spanned by <paramref name="fragment" />, or
    ///     <see langword="null" /> when the collapsed text does not contain it.
    /// </summary>
    public (int First, int Last)? Span(string fragment)
    {
        int start = Text.IndexOf(fragment, StringComparison.Ordinal);
        if (start < 0 || fragment.Length == 0) return null;

        return (LineAt(start), LineAt(start + fragment.Length - 1));
    }
}
