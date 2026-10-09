namespace SlideRule.Codebase;

/// <summary>The extent of one reference in its file: an absolute character offset and a character count.</summary>
// Spelled out rather than positional because this target framework declares no IsExternalInit, so an
// `init` accessor — which a positional record struct synthesizes — will not compile here. The record
// shape is still what is wanted: two same-typed ints a caller could transpose, compared by value.
internal readonly record struct SourceSpan
{
    /// <summary>The span, from <paramref name="start" /> for <paramref name="length" /> characters.</summary>
    /// <param name="start">The absolute character offset the reference starts at.</param>
    /// <param name="length">How many characters it covers.</param>
    internal SourceSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary>Gets the absolute character offset the reference starts at.</summary>
    internal int Start { get; }

    /// <summary>Gets how many characters the reference covers.</summary>
    internal int Length { get; }
}
