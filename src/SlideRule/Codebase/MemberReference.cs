namespace SlideRule.Codebase;

/// <summary>
///     A member some type's source used: the far end of a <see cref="MemberEdge" />. It carries the
///     declaring type and the simple name (the pair a rule banning a member matches on) beside
///     <see cref="SymbolId" />, which identifies the single member observed.
/// </summary>
/// <remarks>
///     <para>
///         Matching and identity are different grains. A ban is written against a declaring
///         type and a name, so one ban covers every overload and there is no signature here; a baseline
///         records <see cref="SymbolId" /> instead, so a grandfathered <c>Wait()</c> rewritten as
///         <c>Wait(timeout)</c> is a new violation and fails the check.
///     </para>
///     <para>
///         Members are recorded at their definition, so <c>Task&lt;int&gt;.Result</c> and
///         <c>Task&lt;string&gt;.Result</c> are one member here. An accessor is recorded as its property or
///         event, and a call written in extension-method form is recorded against the static method that
///         declares it.
///     </para>
/// </remarks>
internal sealed class MemberReference
{
    internal MemberReference(TypeNode containingType, string name, string symbolId, MemberKind kind)
    {
        ContainingType = containingType;
        Name = name;
        SymbolId = symbolId;
        Kind = kind;
    }

    /// <summary>Gets the type that declares the member.</summary>
    public TypeNode ContainingType { get; }

    /// <summary>Gets the member's simple name, with no signature.</summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the identity a baseline records for this member: its Roslyn documentation comment ID —
    ///     <c>M:</c> for a method (its parameters, and for a call in extension-method form its receiver,
    ///     included), <c>P:</c> for a property, <c>F:</c> for a field or enum member, <c>E:</c> for an event —
    ///     or <c>unresolved:{declaring type}.{name}</c> where the compiler gives the member no such ID.
    /// </summary>
    public string SymbolId { get; }

    public MemberKind Kind { get; }
}
