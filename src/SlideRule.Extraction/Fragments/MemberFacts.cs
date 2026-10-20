namespace SlideRule.Extraction.Fragments;

/// <summary>
///     The scalar facts of one declared member (GRAMMAR §4.6), read from the declaring type's
///     <c>OriginalDefinition</c>.
/// </summary>
/// <remarks>
///     Every fact but <see cref="SymbolId" /> means what the <see cref="SlideRule.IMemberInfo" /> member of the
///     same name documents: C# declaration semantics rather than IL's, a kind-scoped fact false or null on the
///     other kinds, and <see cref="IsReadOnly" /> false for a <c>const</c> field. A method's
///     <see cref="ReturnTypeFullName" /> compares equal to the <c>.Returning</c> anchor's
///     <c>TypeName.FullDisplay</c> (GRAMMAR §4.6), and <see cref="Attributes" /> is ordinal by
///     <see cref="FragmentConstruction.ConstructedName" />.
/// </remarks>
internal sealed record MemberFacts(
    string SymbolId,
    string Name,
    MemberKind Kind,
    Accessibility Accessibility,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsSealed,
    bool IsAsync,
    string? ReturnTypeFullName,
    string? MemberTypeFullName,
    IReadOnlyList<ParameterFacts> Parameters,
    IReadOnlyList<FragmentConstruction> Attributes,
    bool HasSetter,
    bool HasInitOnlySetter,
    bool IsReadOnly,
    bool IsConst);
