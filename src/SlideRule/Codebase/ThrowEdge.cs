namespace SlideRule.Codebase;

/// <summary>
///     One type throwing an exception type: a <c>throw</c> in <see cref="TypeEdge.Source" />'s own source throws
///     something whose declared type is <see cref="Thrown" />. Read them from
///     <see cref="CodebaseModel.ThrowEdges" />. Throw expressions count as well as statements —
///     <c>?? throw</c>, a conditional or switch-expression arm, an expression-bodied
///     <c>=&gt; throw new X()</c>.
/// </summary>
/// <remarks>
///     <para>
///         What is recorded is the thrown expression's declared type, so
///         <c>catch (Exception ex) { ...; throw ex; }</c> is a throw of <c>System.Exception</c> whatever was
///         caught. A bare <c>throw;</c> introduces no expression and records nothing, and neither do
///         <c>throw null</c> and a throw of a type parameter.
///     </para>
///     <para>
///         A <c>throw</c> written inside a lambda or a local function counts for the type that encloses it,
///         and one in top-level statements for <c>Program</c>.
///     </para>
///     <para>
///         A throw helper is not a throw: <c>ArgumentNullException.ThrowIfNull(x)</c> is an ordinary call,
///         so it appears as a <see cref="MemberEdge" /> and never here.
///     </para>
/// </remarks>
internal sealed class ThrowEdge : TypeEdge
{
    internal ThrowEdge(TypeNode source, TypeNode thrown, EdgeEvidence evidence)
        : base(source, evidence)
    {
        Thrown = thrown;
    }

    /// <summary>Gets the thrown expression's declared type.</summary>
    public TypeNode Thrown { get; }

    public override TypeNode Target => Thrown;
}
