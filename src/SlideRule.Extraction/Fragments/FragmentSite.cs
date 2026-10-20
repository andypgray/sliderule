using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SlideRule.Extraction.Fragments;

/// <summary>
///     One <c>file:line</c> reference or declaration position inside a fragment: an absolute OS-native
///     file path stored verbatim (no normalization) and a 1-based line. The pure-data counterpart of
///     <see cref="SlideRule.Codebase.SourceLocation" />.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="CompareTo" /> is the pinned site ordering, ordinal by <see cref="File" /> and then by
///         <see cref="Line" />, so a <see cref="System.Collections.Generic.SortedSet{T}" /> of sites orders and
///         de-duplicates identically on the extraction side and the merge side. The ordinal file compare sorts a
///         <c>*.Validation.cs</c> part before its <c>*.cs</c> sibling (<c>'V' &lt; 'c'</c>). A value type,
///         because extraction mints one site per mention and that set discards most of them as duplicates;
///         implementing <see cref="IComparable{T}" /> keeps its <c>Comparer&lt;T&gt;.Default</c> boxing-free.
///     </para>
///     <para>
///         <see cref="Start" /> and <see cref="Length" /> sit outside equality, ordering and the persisted
///         manifest alike. A site minted from syntax carries its span; one built by hand, read back from the
///         cache or moved by its line remap carries none, because the digest the remap goes through has dropped
///         the whitespace a column counts. A span is right or absent.
///     </para>
/// </remarks>
internal readonly record struct FragmentSite(string File, int Line) : IComparable<FragmentSite>
{
    /// <summary>
    ///     The reference's offset in its tree's text; meaningful only where <see cref="Length" /> is non-zero.
    /// </summary>
    internal int Start { get; init; }

    /// <summary>The reference's length in characters, or zero for a site that carries no span.</summary>
    internal int Length { get; init; }

    public int CompareTo(FragmentSite other)
    {
        int byFile = string.CompareOrdinal(File, other.File);
        return byFile != 0 ? byFile : Line.CompareTo(other.Line);
    }

    // Hand-written so that the span stays out of identity: a record struct otherwise compares every field,
    // which would make two mentions on one line two sites and disagree with CompareTo. GetHashCode leaves the
    // span out too, because a set keyed one way and hashed another loses elements silently.
    /// <summary>Whether <paramref name="other" /> is the same <c>file:line</c>, whatever span either carries.</summary>
    public bool Equals(FragmentSite other)
    {
        return Line == other.Line && string.Equals(File, other.File, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            // A default FragmentSite carries a null File. netstandard2.0 has no HashCode.Combine, so the two
            // fields are combined by hand, with 397 as the usual odd prime multiplier.
            return ((File is null ? 0 : File.GetHashCode()) * 397) ^ Line;
        }
    }

    /// <summary>The site of a syntax node: its tree's path and the 1-based line its span starts on.</summary>
    internal static FragmentSite Of(SyntaxNode node)
    {
        return Of(node.SyntaxTree, node.Span);
    }

    /// <summary>
    ///     The site of <paramref name="span" /> inside <paramref name="tree" />, carrying that span beside the
    ///     line it starts on.
    /// </summary>
    /// <remarks>
    ///     The one place a syntax site's 0-based Roslyn line becomes the 1-based <see cref="Line" />. Reading the
    ///     line span off the tree also skips allocating a <see cref="Location" /> per mention.
    /// </remarks>
    internal static FragmentSite Of(SyntaxTree tree, TextSpan span)
    {
        int line = tree.GetLineSpan(span).StartLinePosition.Line + 1;
        return new FragmentSite(tree.FilePath, line) { Start = span.Start, Length = span.Length };
    }
}
