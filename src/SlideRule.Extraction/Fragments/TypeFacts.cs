using SlideRule.Codebase;

namespace SlideRule.Extraction.Fragments;

/// <summary>
///     The scalar shape facts of one type, shared by a fragment's declared types and its externals, and read
///     from the symbol's <c>OriginalDefinition</c>.
/// </summary>
internal sealed record TypeFacts(
    string FullName,
    string SymbolId,
    string Name,
    string Namespace,
    TypeKind Kind,
    Accessibility Accessibility,
    bool IsSealed,
    bool IsStatic,
    bool IsAbstract,
    bool IsRecord,
    bool IsGenerated)
{
    /// <summary>
    ///     Mints the shallow node these facts describe; the merge populates a declared node's hierarchy, sites
    ///     and members afterwards.
    /// </summary>
    public TypeNode ToTypeNode(string projectName, bool isExternal)
    {
        return new TypeNode(
            fullName: FullName,
            symbolId: SymbolId,
            name: Name,
            @namespace: Namespace,
            kind: Kind,
            accessibility: Accessibility,
            isSealed: IsSealed,
            isStatic: IsStatic,
            isAbstract: IsAbstract,
            isRecord: IsRecord,
            isGenerated: IsGenerated,
            projectName,
            isExternal);
    }
}
