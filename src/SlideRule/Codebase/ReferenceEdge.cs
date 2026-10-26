namespace SlideRule.Codebase;

/// <summary>
///     One type referencing another: <see cref="TypeEdge.Source" />'s own source names
///     <see cref="Target" /> as a type, or uses one of its members. Read them from
///     <see cref="CodebaseModel.Edges" />.
/// </summary>
/// <remarks>
///     Types are recorded at their definition: source naming <c>IHandler&lt;Order&gt;</c> gives an edge to
///     <c>IHandler&lt;T&gt;</c> and another to <c>Order</c>, never one to the construction itself.
/// </remarks>
internal sealed class ReferenceEdge : TypeEdge
{
    internal ReferenceEdge(TypeNode source, TypeNode target, EdgeEvidence evidence)
        : base(source, evidence)
    {
        Target = target;
    }

    /// <summary>Gets the referenced type.</summary>
    public override TypeNode Target { get; }
}
