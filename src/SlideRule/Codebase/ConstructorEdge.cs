namespace SlideRule.Codebase;

/// <summary>
///     One type creating another: <see cref="Source" />'s own source creates <see cref="Constructed" /> with
///     an object-creation expression, either <c>new Foo()</c> or a target-typed <c>new()</c>. Read them from
///     <see cref="CodebaseModel.ConstructorEdges" />.
/// </summary>
/// <remarks>
///     <para>
///         Types are recorded at their definition, so <c>new Box&lt;int&gt;()</c> gives an edge to
///         <c>Box&lt;&gt;</c>.
///     </para>
///     <para>
///         Five spellings never appear here, none of them a creation of the type written: an attribute
///         applied to a declaration, a <c>: base(...)</c> or <c>: this(...)</c> initializer, delegate
///         creation (<c>new Action(M)</c> and its target-typed form), a <c>with</c> expression, and array
///         creation. An object a container or reflection creates is invisible either way — but a factory
///         lambda that itself writes <c>new</c>, as in <c>AddScoped(sp =&gt; new Foo(...))</c>, is an
///         ordinary creation and is recorded.
///     </para>
/// </remarks>
internal sealed class ConstructorEdge : IEdgeEnds
{
    internal ConstructorEdge(TypeNode source, TypeNode constructed, EdgeEvidence evidence)
    {
        Source = source;
        Constructed = constructed;
        Evidence = evidence;
    }

    /// <summary>Gets the constructing type.</summary>
    public TypeNode Source { get; }

    /// <summary>Gets the type that is created.</summary>
    public TypeNode Constructed { get; }

    TypeNode IEdgeEnds.Target => Constructed;

    public IReadOnlyList<SourceLocation> Sites => Evidence.Sites;

    internal EdgeEvidence Evidence { get; }
}
