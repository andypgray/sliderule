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
///         <see cref="CompareTo" /> is the pinned site ordering — ordinal by <see cref="File" />, then by
///         <see cref="Line" /> — so a <see cref="System.Collections.Generic.SortedSet{T}" /> of sites both
///         orders and de-duplicates identically on the per-input extraction side and the merge side. Note the
///         ordinal file compare means a <c>*.Validation.cs</c> part sorts before its <c>*.cs</c> sibling
///         (<c>'V' &lt; 'c'</c>).
///     </para>
///     <para>
///         <see cref="Start" /> and <see cref="Length" /> are a payload the site carries rather than part of
///         what it is: they sit outside equality, ordering and the persisted manifest alike. A stored site
///         comes back through the cache's line remap, which can move a line but cannot keep a column valid —
///         the digest it remaps through has dropped the whitespace the column counts — and no reader of the
///         cache wants one. What does want one is the analyzer, which extracts live and puts its squiggle on
///         the reference the extractor recorded rather than on the whole line, so a site that was minted from
///         syntax carries the span and one built by hand, read back from disk or moved through a remap
///         carries none: a span is right or absent.
///     </para>
///     <para>
///         A value type, because extraction mints one site per <em>mention</em> and the overwhelming majority
///         are immediately discarded as duplicates by the SortedSet they are offered to. Implementing
///         <see cref="IComparable{T}" /> is what keeps that set's <c>Comparer&lt;T&gt;.Default</c>
///         boxing-free.
///     </para>
/// </remarks>
internal readonly record struct FragmentSite(string File, int Line) : IComparable<FragmentSite>
{
    /// <summary>
    ///     The absolute character offset the recorded reference starts at in its tree's text; meaningful
    ///     only where <see cref="Length" /> is non-zero.
    /// </summary>
    internal int Start { get; init; }

    /// <summary>
    ///     How many characters the recorded reference covers, or zero for a site that carries no span.
    /// </summary>
    internal int Length { get; init; }

    public int CompareTo(FragmentSite other)
    {
        int byFile = string.CompareOrdinal(File, other.File);
        return byFile != 0 ? byFile : Line.CompareTo(other.Line);
    }

    // Hand-written so that the span stays out of identity: a record struct otherwise compares every field,
    // which would make two mentions on one line two sites and disagree with CompareTo. The pair is written
    // together because an Equals without a GetHashCode beside it is CS8851, and because a set keyed one way
    // and hashed another loses elements silently.
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
    ///     The site of <paramref name="span" /> inside <paramref name="tree" /> — for the callers holding a
    ///     token or a <see cref="Location" /> rather than a node, which pass its span — carrying that span
    ///     beside the line it starts on.
    /// </summary>
    /// <remarks>
    ///     The <c>+ 1</c> is the whole reason both overloads exist: Roslyn reports a 0-based line and the
    ///     <see cref="Line" /> this record documents is 1-based, so the conversion is written here once rather
    ///     than in every site pass, where any one copy could drift from the contract. Reading the line span off
    ///     the tree also skips allocating an intermediate <see cref="Location" /> per mention.
    /// </remarks>
    internal static FragmentSite Of(SyntaxTree tree, TextSpan span)
    {
        int line = tree.GetLineSpan(span).StartLinePosition.Line + 1;
        return new FragmentSite(tree.FilePath, line) { Start = span.Start, Length = span.Length };
    }
}
