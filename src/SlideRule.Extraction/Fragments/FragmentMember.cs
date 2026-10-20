using SlideRule.Codebase;

namespace SlideRule.Extraction.Fragments;

/// <summary>
///     One declared member of a fragment's declared type (GRAMMAR §4.6): its facts and its declaration sites.
/// </summary>
internal sealed record FragmentMember(
    MemberFacts Facts,
    IReadOnlyList<FragmentSite> DeclarationSites)
{
    /// <summary>Mints the model's node for this member, under <paramref name="declaringType" />.</summary>
    /// <remarks>
    ///     Every list keeps the order the fragment stored it in. The attributes stay string-side: unlike a
    ///     type's, a member's attributes mint no external node.
    /// </remarks>
    public MemberNode ToMemberNode(TypeNode declaringType)
    {
        IReadOnlyList<SourceLocation> declarationSites = DeclarationSites.ToLocations();
        IReadOnlyList<string> filePaths = DeclarationSites.FilePaths();
        List<ParameterNode> parameters = Facts.Parameters.Select(p => new ParameterNode(p.Name, p.TypeFullName)).ToList();
        List<AttributeNode> attributes = Facts.Attributes.Select(a => new AttributeNode(a.DefinitionFullName, a.ConstructedName)).ToList();

        return new MemberNode(
            declaringType,
            symbolId: Facts.SymbolId,
            name: Facts.Name,
            kind: Facts.Kind,
            accessibility: Facts.Accessibility,
            isStatic: Facts.IsStatic,
            isAbstract: Facts.IsAbstract,
            isVirtual: Facts.IsVirtual,
            isAsync: Facts.IsAsync,
            returnTypeFullName: Facts.ReturnTypeFullName,
            memberTypeFullName: Facts.MemberTypeFullName,
            declarationSites,
            filePaths,
            parameters,
            attributes,
            hasSetter: Facts.HasSetter,
            hasInitOnlySetter: Facts.HasInitOnlySetter,
            isReadOnly: Facts.IsReadOnly,
            isConst: Facts.IsConst,
            isOverride: Facts.IsOverride,
            isSealed: Facts.IsSealed);
    }
}
