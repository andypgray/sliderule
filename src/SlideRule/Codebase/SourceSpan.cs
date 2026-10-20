namespace SlideRule.Codebase;

/// <summary>The extent of one reference in its file: an absolute character offset and a character count.</summary>
internal readonly record struct SourceSpan
{
    internal SourceSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    internal int Start { get; }

    internal int Length { get; }
}
