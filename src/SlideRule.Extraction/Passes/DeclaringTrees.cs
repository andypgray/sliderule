using Microsoft.CodeAnalysis;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     Which types each of a compilation's syntax trees holds a declaring part of — the lookup a
///     per-document extraction is steered by, built once for the compilation and read once per document.
/// </summary>
/// <remarks>
///     <para>
///         The walk is <see cref="FragmentExtractor.DeclaredTypes" />'s, shared rather than restated: an
///         index built from a second walk could disagree with extraction about which types exist. It is
///         also why the walk is over namespaces rather than over type-declaration syntax — a
///         top-level-statements <c>Program</c> is declared by no type declaration at all, and a syntax walk
///         would leave the only document that could carry it empty.
///     </para>
///     <para>
///         A type is keyed under <em>every</em> tree holding one of its declaring parts, so each part of a
///         partial type names the whole type. That is what lets a document's extraction carry the type
///         whole — every part's sites and edges — rather than the slice one file can see.
///     </para>
///     <para>
///         The screen that decides which declared types are rule subjects is deliberately not applied
///         here: this states what a tree declares, and extraction asks that question where it already
///         does, over whatever it is handed.
///     </para>
/// </remarks>
internal sealed class DeclaringTrees
{
    private static readonly IReadOnlyList<INamedTypeSymbol> None = Array.Empty<INamedTypeSymbol>();

    // Keyed by tree reference, which is the identity the compilation and its semantic models use: two
    // trees parsed from one path are two documents here, exactly as they are to the compiler.
    private readonly Dictionary<SyntaxTree, List<INamedTypeSymbol>> _byTree = new();

    private DeclaringTrees()
    {
    }

    /// <summary>
    ///     Every type <paramref name="compilation" /> declares, under each syntax tree that holds one of its
    ///     declaring parts.
    /// </summary>
    internal static DeclaringTrees Of(Compilation compilation)
    {
        var declaringTrees = new DeclaringTrees();

        foreach (INamedTypeSymbol type in FragmentExtractor.DeclaredTypes(compilation))
        foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
            declaringTrees.Add(reference.SyntaxTree, type);

        return declaringTrees;
    }

    /// <summary>
    ///     The types declared in <paramref name="tree" />, in declaration-walk order; empty when it declares
    ///     none.
    /// </summary>
    internal IReadOnlyList<INamedTypeSymbol> In(SyntaxTree tree)
    {
        return _byTree.TryGetValue(tree, out List<INamedTypeSymbol>? types) ? types : None;
    }

    // At most one entry per (tree, type): a type declared partially twice in one file has two declaring
    // references into that file, and extraction walks every part of a type it is handed once, so a second
    // entry would extract it twice. Comparing the list's last entry is the whole test, because one type's
    // references are added consecutively and no other type can land between them.
    private void Add(SyntaxTree tree, INamedTypeSymbol type)
    {
        if (!_byTree.TryGetValue(tree, out List<INamedTypeSymbol>? types))
        {
            types = [];
            _byTree[tree] = types;
        }

        int last = types.Count - 1;
        if (last >= 0 && SymbolEqualityComparer.Default.Equals(types[last], type)) return;

        types.Add(type);
    }
}
