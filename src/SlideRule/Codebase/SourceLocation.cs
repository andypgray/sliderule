namespace SlideRule.Codebase;

/// <summary>
///     A place in the source: a file path and a 1-based line number. Every site list in the extracted model
///     is made of these (where an edge occurs, where a type or a member is declared), and a violation
///     carries the ones it was found at, so a report can point straight at the code.
/// </summary>
internal sealed class SourceLocation
{
    internal SourceLocation(string filePath, int line, SourceSpan? span = null)
    {
        FilePath = filePath;
        Line = line;
        Span = span;
    }

    /// <summary>
    ///     Gets the file path exactly as the compiler recorded it, which is absolute for a model extracted from
    ///     a solution on disk. The CLI makes it relative to the solution directory before printing it.
    /// </summary>
    public string FilePath { get; }

    /// <summary>Gets the 1-based line number.</summary>
    public int Line { get; }

    /// <summary>
    ///     Gets the extent of the reference on that line, or null where none was recorded — which is every
    ///     location that came from the persisted cache, since the span is never written to it, and every one
    ///     in a file whose edit moved its sites through a line remap, which drops a span rather than keep it
    ///     stale.
    /// </summary>
    // Reported by nothing: the line is the position every renderer, baseline key and report agrees on, and
    // the span is only ever the analyzer's squiggle.
    internal SourceSpan? Span { get; }

    /// <summary>Returns the position as <c>{FilePath}:{Line}</c>.</summary>
    // The rendered form is pinned by tests, which compare two site lists through it. The report renderers do
    // not call it: each makes the path relative to the solution directory itself before printing.
    public override string ToString()
    {
        return $"{FilePath}:{Line}";
    }
}
