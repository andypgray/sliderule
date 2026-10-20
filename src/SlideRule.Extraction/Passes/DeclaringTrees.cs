using Microsoft.CodeAnalysis;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     Which types each of a compilation's syntax trees holds a declaring part of, built once for the
///     compilation and read once per document.
/// </summary>
/// <remarks>
///     The walk is <see cref="FragmentExtractor.DeclaredTypes" />'s, so this index and extraction agree about
///     which types exist, a top-level-statements <c>Program</c> included, which no type declaration declares. A
///     type is keyed under every tree holding one of its declaring parts, so a document's extraction carries a
///     partial type whole rather than the slice one file can see. No rule-subject screen applies here:
///     extraction screens whatever it is handed.
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

    internal static DeclaringTrees Of(Compilation compilation)
    {
        var declaringTrees = new DeclaringTrees();

        foreach (INamedTypeSymbol type in FragmentExtractor.DeclaredTypes(compilation))
        foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
            declaringTrees.Add(reference.SyntaxTree, type);

        return declaringTrees;
    }

    /// <summary>The types declared in <paramref name="tree" />, in declaration-walk order.</summary>
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
