namespace SlideRule.Codebase;

/// <summary>
///     The two type ends of an edge whose far end is a type: every edge family but the member use, whose far
///     end is a member. Each family names its far end for what it is — the constructed, caught or exposed
///     type — and answers <see cref="Target" /> with it, so one walk can ask any of them which copies an
///     instance reached.
/// </summary>
internal interface IEdgeEnds
{
    /// <summary>The type whose own source makes the edge.</summary>
    TypeNode Source { get; }

    /// <summary>The type at the far end.</summary>
    TypeNode Target { get; }
}
