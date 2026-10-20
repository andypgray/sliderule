namespace SlideRule.Codebase;

/// <summary>A place in the source: a file path and a 1-based line number.</summary>
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
    ///     a solution on disk.
    /// </summary>
    public string FilePath { get; }

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
    // The rendered form is pinned by tests, which compare two site lists through it.
    public override string ToString()
    {
        return $"{FilePath}:{Line}";
    }
}
