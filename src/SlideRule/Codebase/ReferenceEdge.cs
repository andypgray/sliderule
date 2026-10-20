namespace SlideRule.Codebase;

/// <summary>
///     One type referencing another: <see cref="Source" />'s own source names <see cref="Target" /> as a
///     type, or uses one of its members. Read them from <see cref="CodebaseModel.Edges" />.
/// </summary>
/// <remarks>
///     Types are recorded at their definition: source naming <c>IHandler&lt;Order&gt;</c> gives an edge to
///     <c>IHandler&lt;T&gt;</c> and another to <c>Order</c>, never one to the construction itself.
/// </remarks>
internal sealed class ReferenceEdge : IEdgeEnds
{
    internal ReferenceEdge(TypeNode source, TypeNode target, EdgeEvidence evidence)
    {
        Source = source;
        Target = target;
        Evidence = evidence;
    }

    /// <summary>Gets the referencing type.</summary>
    public TypeNode Source { get; }

    /// <summary>Gets the referenced type.</summary>
    public TypeNode Target { get; }

    public IReadOnlyList<SourceLocation> Sites => Evidence.Sites;

    internal EdgeEvidence Evidence { get; }
}
