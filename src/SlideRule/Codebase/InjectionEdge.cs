namespace SlideRule.Codebase;

/// <summary>
///     One type taking another as a constructor parameter: an instance constructor <see cref="Source" />
///     declares has a parameter of <see cref="Injected" />'s type. Read them from
///     <see cref="CodebaseModel.InjectionEdges" />. The parameter being declared is the whole fact, whether
///     or not any constructor body does anything with it, and a primary constructor's parameters count.
/// </summary>
/// <remarks>
///     Parameter types are recorded at their definition and taken apart: <c>IEnumerable&lt;IFoo&gt;</c>
///     gives an edge to <c>IEnumerable&lt;&gt;</c> and another to <c>IFoo</c>, and an array gives its
///     element type. The compiler's default parameterless constructor, a record's copy constructor and static
///     constructors declare nothing to record, and enums and delegates contribute none.
/// </remarks>
internal sealed class InjectionEdge : IEdgeEnds
{
    internal InjectionEdge(TypeNode source, TypeNode injected, EdgeEvidence evidence)
    {
        Source = source;
        Injected = injected;
        Evidence = evidence;
    }

    /// <summary>Gets the injecting type — the one whose constructor declares the parameter.</summary>
    public TypeNode Source { get; }

    /// <summary>Gets the parameter's type.</summary>
    public TypeNode Injected { get; }

    TypeNode IEdgeEnds.Target => Injected;

    /// <summary>Gets the declaration line of each parameter of this type.</summary>
    public IReadOnlyList<SourceLocation> Sites => Evidence.Sites;

    internal EdgeEvidence Evidence { get; }
}
