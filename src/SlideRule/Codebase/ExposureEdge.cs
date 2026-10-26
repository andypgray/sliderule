namespace SlideRule.Codebase;

/// <summary>
///     One type naming another in its public API: a member of <see cref="TypeEdge.Source" /> names
///     <see cref="Exposed" /> in a signature position — a method's return type or one of its parameter
///     types, or a property, field or event's type. Read them from
///     <see cref="CodebaseModel.ExposureEdges" />. Its sites are the declaration line of each exposing member.
/// </summary>
/// <remarks>
///     <para>
///         Only what a caller outside the assembly could reach counts: the member must be public and every
///         type enclosing it public too, so a <c>public</c> member of an <c>internal</c> type exposes
///         nothing. A constructor's parameters belong to
///         <see cref="CodebaseModel.InjectionEdges" /> instead, and base types and interface lists are
///         inheritance rather than signatures; neither appears here. Nor do indexers, operators,
///         conversions, accessors, explicit interface implementations, compiler-generated members, or a
///         <c>void</c> return, which names no type.
///     </para>
///     <para>
///         Signature types are recorded at their definition and taken apart: <c>Task&lt;Order&gt;</c> gives
///         edges to <c>Task&lt;&gt;</c> and <c>Order</c>, an array gives its element type,
///         <c>(Order, Widget)</c> gives the open tuple under its display form <c>(T1, T2)</c> plus both
///         element types, and <c>int?</c> gives <c>System.Nullable&lt;T&gt;</c> and <c>System.Int32</c>.
///         Framework types are recorded like any other, so <c>public string Name</c> exposes
///         <c>System.String</c>. Where the signature spells no type name (a tuple, a <c>?</c>, the keyword
///         <c>int</c>) the exposure edge has no ordinary reference beside it.
///     </para>
///     <para>
///         What is recorded is the declared signature, never what flows through it: a member returning
///         <c>object</c> exposes <c>System.Object</c> and nothing else, whatever the caller casts it to.
///     </para>
/// </remarks>
internal sealed class ExposureEdge : TypeEdge
{
    internal ExposureEdge(TypeNode source, TypeNode exposed, EdgeEvidence evidence)
        : base(source, evidence)
    {
        Exposed = exposed;
    }

    /// <summary>Gets the type named in the signature.</summary>
    public TypeNode Exposed { get; }

    public override TypeNode Target => Exposed;
}
