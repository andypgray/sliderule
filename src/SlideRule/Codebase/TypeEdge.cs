namespace SlideRule.Codebase;

/// <summary>
///     An edge whose far end is a type. Each family names its far end for what it is and answers
///     <see cref="Target" /> with it.
/// </summary>
internal abstract class TypeEdge : IEdgeEnds
{
    protected TypeEdge(TypeNode source, EdgeEvidence evidence)
    {
        Source = source;
        Evidence = evidence;
    }

    /// <summary>Gets the type whose own source makes the edge.</summary>
    public TypeNode Source { get; }

    /// <summary>Gets the type at the far end.</summary>
    public abstract TypeNode Target { get; }

    public IReadOnlyList<SourceLocation> Sites => Evidence.Sites;

    internal EdgeEvidence Evidence { get; }
}
