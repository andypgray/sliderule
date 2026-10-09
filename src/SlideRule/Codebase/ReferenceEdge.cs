namespace SlideRule.Codebase;

/// <summary>
///     One type referencing another: <see cref="Source" />'s own source names <see cref="Target" /> as a
///     type, or uses one of its members. Read them from <see cref="CodebaseModel.Edges" />.
/// </summary>
/// <remarks>
///     A type never references itself here, and both ends are the very type instances
///     <see cref="CodebaseModel.Types" /> lists, so compare them by reference rather than by name. Types are
///     recorded at their definition: source naming <c>IHandler&lt;Order&gt;</c> gives an edge to
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

    /// <summary>
    ///     Gets each distinct place the reference occurs, ordered by file then line. Two references to the same
    ///     target on one line count as one site.
    /// </summary>
    public IReadOnlyList<SourceLocation> Sites => Evidence.Sites;

    internal EdgeEvidence Evidence { get; }
}
