using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Tags;

namespace SlideRule.SpecCompletion;

/// <summary>
///     The names a slot can be filled with, drawn from the compilation the spec project is written in.
/// </summary>
/// <remarks>
///     <para>
///         <b>What is offered is the referenced product's names, not the spec project's own.</b> A spec
///         project declares rules and nothing a rule anchors on, so its own namespaces and types are the
///         one part of the compilation an author never writes into a slot; the contract's are the other,
///         except in the repository that writes a spec over the contract itself. Both are taken out, and
///         everything else the compilation can see — the referenced projects, their packages, the
///         framework — is offered, with the referenced projects ranked first.
///     </para>
///     <para>
///         <b>Product is decided by project reference and matched by assembly name.</b> A workspace hands
///         a project's reference either as a compilation or as a skeleton image depending on what it has
///         built, and the two are different symbols; the name is the one thing both carry. A host whose
///         workspace populates no project references loses the ranking and offers the same names
///         unranked, which is the right way for that to degrade.
///     </para>
///     <para>
///         <b>A name is offered whether or not anything matches beneath it.</b> A namespace with no type
///         under it is still a namespace an author can write a glob over, and a walk that dead-ends is
///         still the walk the author is in the middle of. Filtering by what lies beneath would make the
///         list depend on how much of the name had been typed.
///     </para>
/// </remarks>
internal sealed class NameUniverse
{
    /// <summary>
    ///     A type's own name as a slot spells it — the leaf of the full name the extractor renders, with
    ///     declared type-parameter names and no namespace.
    /// </summary>
    /// <remarks>
    ///     This and the path before it compose to exactly the full name a report prints, which is the name
    ///     an anchor is compared against. Type parameters are included because the anchor names a
    ///     definition; special types are not spelled as keywords, because a keyword names nothing the
    ///     comparison would match.
    /// </remarks>
    private static readonly SymbolDisplayFormat LeafFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameOnly,
        SymbolDisplayGenericsOptions.IncludeTypeParameters);

    /// <summary>
    ///     The simple names one assembly declares, kept per assembly symbol for as long as the symbol
    ///     lives.
    /// </summary>
    /// <remarks>
    ///     The exact-name slot is the one that walks a whole assembly rather than one namespace, so it is
    ///     the one worth remembering. Held on the instance and never in a static field: an editor keeps
    ///     one provider per workspace and a static table would hold symbols from every solution the
    ///     process has opened.
    /// </remarks>
    private readonly ConditionalWeakTable<IAssemblySymbol, IReadOnlyList<SimpleName>> _simpleNames = new();

    /// <summary>
    ///     What the slot can be filled with at this point in the literal, in the order the list shows.
    /// </summary>
    /// <param name="project">The document's project, whose references decide what counts as product.</param>
    /// <param name="compilation">The compilation the names are drawn from.</param>
    /// <param name="marker">The marker type, whose assembly is the contract to leave out.</param>
    /// <param name="kind">The slot being filled.</param>
    /// <param name="path">What was written before the segment the caret is in.</param>
    /// <param name="cancellationToken">The token the walk observes, once per product assembly.</param>
    internal IReadOnlyList<Offer> Offers(
        Project project,
        Compilation compilation,
        INamedTypeSymbol marker,
        SlotKind kind,
        string path,
        CancellationToken cancellationToken)
    {
        // A wildcard already written into the path makes everything after it a glob rather than a name,
        // and there is no namespace to look names up under.
        if (path.IndexOf('*') >= 0) return [];

        HashSet<string> product = ProductAssemblyNames(project);
        IAssemblySymbol contract = marker.ContainingAssembly;
        bool contractIsProduct = contract is not null && product.Contains(contract.Name);
        Exclusions exclusions = new(compilation.Assembly, contractIsProduct ? null : contract);

        if (kind == SlotKind.ExactTypeName)
            // An exact name is a simple name: a dotted one matches nothing, so once a dot is written
            // there is nothing here that would be right to offer.
            return path.Length == 0
                ? SimpleNamesOf(compilation, product, cancellationToken)
                : [];

        if (Resolve(compilation.GlobalNamespace, path) is not { } container) return [];

        bool pattern = kind == SlotKind.NamespacePattern;
        if (pattern && container is not INamespaceSymbol) return [];

        List<Offer> offers = [];
        if (container is INamespaceSymbol region)
            foreach (INamespaceSymbol child in region.GetNamespaceMembers())
            {
                if (!Offerable(child, exclusions)) continue;

                bool fromProduct = IsProduct(child, product);
                offers.Add(Offer.NamespaceStep(child.Name, fromProduct, DeclaringAssembly(child, exclusions)));
                if (pattern) offers.Add(Offer.Subtree(child.Name, fromProduct));
            }

        if (pattern)
        {
            offers.Add(Offer.LoneStar());
            return Ordered(offers);
        }

        foreach (INamedTypeSymbol type in container.GetTypeMembers())
        {
            if (!Nameable(type)) continue;
            if (exclusions.Cover(type.ContainingAssembly)) continue;
            if (!Accepts(kind, type)) continue;

            string leaf = type.OriginalDefinition.ToDisplayString(LeafFormat);
            offers.Add(Offer.Type(leaf, IsProduct(type, product), type.ContainingAssembly?.Name, TagOf(type)));
        }

        return Ordered(offers);
    }

    // The order the list is shown in, which is the order the sort text spells: everything the referenced
    // projects declare, then everything else, each ordinally by the text that would be inserted. A
    // subtree form sorts straight after the step it belongs to, because a dot precedes every letter.
    private static List<Offer> Ordered(List<Offer> offers)
    {
        offers.Sort(static (left, right) =>
        {
            int rank = left.Rank.CompareTo(right.Rank);

            return rank != 0 ? rank : string.CompareOrdinal(left.Insert, right.Insert);
        });

        return offers;
    }

    // Every simple name the referenced projects declare, once each. Nested types contribute their own
    // name rather than a dotted one, because that is what the slot compares against.
    private IReadOnlyList<Offer> SimpleNamesOf(
        Compilation compilation, HashSet<string> product, CancellationToken cancellationToken)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<Offer> offers = [];

        foreach (IAssemblySymbol assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!product.Contains(assembly.Name)) continue;

            cancellationToken.ThrowIfCancellationRequested();

            foreach (SimpleName name in _simpleNames.GetValue(assembly, Walk))
            {
                if (!seen.Add(name.Name)) continue;

                offers.Add(Offer.SimpleName(name.Name, name.Tag));
            }
        }

        return Ordered(offers);
    }

    private static IReadOnlyList<SimpleName> Walk(IAssemblySymbol assembly)
    {
        List<SimpleName> names = [];
        Collect(assembly.GlobalNamespace, names);

        return names;
    }

    private static void Collect(INamespaceSymbol region, List<SimpleName> names)
    {
        foreach (INamespaceSymbol child in region.GetNamespaceMembers()) Collect(child, names);

        foreach (INamedTypeSymbol type in region.GetTypeMembers()) CollectType(type, names);
    }

    private static void CollectType(INamedTypeSymbol type, List<SimpleName> names)
    {
        if (Nameable(type)) names.Add(new SimpleName(type.Name, TagOf(type)));

        foreach (INamedTypeSymbol nested in type.GetTypeMembers()) CollectType(nested, names);
    }

    // The container the path names, walked one segment at a time over what this universe itself offers:
    // a namespace by its name, a type by the leaf a list would have shown for it. Resolving the path the
    // same way it was written is what makes a nested type under a generic one reachable, since its
    // segment carries the declared type parameters.
    private static INamespaceOrTypeSymbol? Resolve(INamespaceSymbol global, string path)
    {
        INamespaceOrTypeSymbol container = global;
        if (path.Length == 0) return container;

        foreach (string segment in path.Split('.'))
        {
            if (segment.Length == 0) return null;
            if (Step(container, segment) is not { } next) return null;

            container = next;
        }

        return container;
    }

    private static INamespaceOrTypeSymbol? Step(INamespaceOrTypeSymbol container, string segment)
    {
        if (container is INamespaceSymbol region)
            foreach (INamespaceSymbol child in region.GetNamespaceMembers())
                if (string.Equals(child.Name, segment, StringComparison.Ordinal))
                    return child;

        foreach (INamedTypeSymbol type in container.GetTypeMembers())
        {
            string leaf = type.OriginalDefinition.ToDisplayString(LeafFormat);
            if (string.Equals(leaf, segment, StringComparison.Ordinal)) return type;
        }

        return null;
    }

    // Which types a slot can be filled with. A base type is a class that can be derived from, so a sealed
    // or static one names nothing a subject could reach; an attribute anchor is a class that reaches
    // System.Attribute, which leaves Attribute itself out because nothing declares it; a return type may
    // be void and a parameter type may not.
    private static bool Accepts(SlotKind kind, INamedTypeSymbol type)
    {
        return kind switch
        {
            SlotKind.InterfaceName => type.TypeKind == TypeKind.Interface,
            SlotKind.BaseTypeName => type.TypeKind == TypeKind.Class && !type.IsSealed && !type.IsStatic,
            SlotKind.AttributeName => type.TypeKind == TypeKind.Class && ReachesAttribute(type),
            SlotKind.ReturnTypeName => true,
            SlotKind.ParameterTypeName => type.SpecialType != SpecialType.System_Void,
            _ => false
        };
    }

    private static bool ReachesAttribute(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? ancestor = type.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            bool inSystem = ancestor.ContainingNamespace is
                { Name: "System", ContainingNamespace.IsGlobalNamespace: true };
            if (inSystem && string.Equals(ancestor.Name, "Attribute", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    // A name an author could write. Accessibility is deliberately not read: a spec anchors on the
    // internal types of the project it governs as readily as on the public ones, and a list that hid them
    // would hide exactly the rules that are worth writing.
    private static bool Nameable(INamedTypeSymbol type)
    {
        return type.CanBeReferencedByName
               && type.TypeKind != TypeKind.Error
               && type.TypeKind != TypeKind.Submission;
    }

    private static bool Offerable(INamespaceSymbol region, Exclusions exclusions)
    {
        if (!region.CanBeReferencedByName) return false;

        // A namespace several assemblies contribute to is offered while any one of them is a name worth
        // offering. Only a namespace that lives entirely in the spec project or entirely in the contract
        // is left out, which is what keeps a product namespace of the same name visible.
        foreach (INamespaceSymbol constituent in region.ConstituentNamespaces)
            if (!exclusions.Cover(constituent.ContainingAssembly))
                return true;

        return false;
    }

    private static bool IsProduct(INamespaceSymbol region, HashSet<string> product)
    {
        foreach (INamespaceSymbol constituent in region.ConstituentNamespaces)
            if (constituent.ContainingAssembly is { } assembly && product.Contains(assembly.Name))
                return true;

        return false;
    }

    private static bool IsProduct(INamedTypeSymbol type, HashSet<string> product)
    {
        return type.ContainingAssembly is { } assembly && product.Contains(assembly.Name);
    }

    // The assembly a name came from, where exactly one did. A namespace several assemblies contribute to
    // has no one answer, and a list row claiming one of them would be telling the author something
    // untrue about where the rest of it lives.
    private static string? DeclaringAssembly(INamespaceSymbol region, Exclusions exclusions)
    {
        string? declaring = null;
        foreach (INamespaceSymbol constituent in region.ConstituentNamespaces)
        {
            if (constituent.ContainingAssembly is not { } assembly) continue;
            if (exclusions.Cover(assembly)) continue;
            if (declaring is null)
            {
                declaring = assembly.Name;
                continue;
            }

            if (!string.Equals(declaring, assembly.Name, StringComparison.Ordinal)) return null;
        }

        return declaring;
    }

    private static string TagOf(INamedTypeSymbol type)
    {
        return type.TypeKind switch
        {
            TypeKind.Interface => WellKnownTags.Interface,
            TypeKind.Struct => WellKnownTags.Structure,
            TypeKind.Enum => WellKnownTags.Enum,
            TypeKind.Delegate => WellKnownTags.Delegate,
            TypeKind.Module => WellKnownTags.Module,
            _ => WellKnownTags.Class
        };
    }

    // The assembly names of everything the project references, transitively. Names rather than symbols,
    // because the two ends of a project reference are different symbols depending on what the workspace
    // has built, and only the name is the same on both.
    private static HashSet<string> ProductAssemblyNames(Project project)
    {
        Solution solution = project.Solution;
        IEnumerable<ProjectId> dependencies =
            solution.GetProjectDependencyGraph().GetProjectsThatThisProjectTransitivelyDependsOn(project.Id);

        // Ignoring case, as .NET matches simple assembly names. These come from each project's AssemblyName and
        // are looked up with names read off assembly symbols, two sources that need not agree on case.
        // DeclaringAssembly and the build-tier analyzer's own-project check compare two names that were both
        // read off symbols, so ordinal is exact there.
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectId id in dependencies)
            if (solution.GetProject(id) is { } dependency)
                names.Add(dependency.AssemblyName);

        return names;
    }

    // The assemblies whose names are never offered: the spec project's own, and the contract's wherever
    // the spec does not itself govern the contract.
    private readonly struct Exclusions(IAssemblySymbol own, IAssemblySymbol? contract)
    {
        internal bool Cover(IAssemblySymbol? assembly)
        {
            if (assembly is null) return false;

            return SymbolEqualityComparer.Default.Equals(assembly, own)
                   || SymbolEqualityComparer.Default.Equals(assembly, contract);
        }
    }

    private readonly struct SimpleName(string name, string tag)
    {
        internal string Name { get; } = name;

        internal string Tag { get; } = tag;
    }
}

/// <summary>Which shape an offered name takes in the list, and so which rules it carries.</summary>
internal enum OfferShape
{
    /// <summary>One segment of a namespace, which a dot commits and continues from.</summary>
    NamespaceStep,

    /// <summary>A namespace and everything beneath it, written with a trailing <c>.*</c>.</summary>
    Subtree,

    /// <summary>Everything at this point, written as a lone <c>*</c>.</summary>
    LoneStar,

    /// <summary>One type, spelled as the leaf of the name a report prints.</summary>
    Type,

    /// <summary>One type's simple name, which is what an exact-name slot compares against.</summary>
    SimpleName
}

/// <summary>One name the universe offers, before it is turned into an item the host shows.</summary>
/// <param name="insert">The text a commit writes into the segment.</param>
/// <param name="shape">Which shape the name takes, which decides the commit and filter rules.</param>
/// <param name="rank">0 for a name the referenced projects declare and for the lone star, 1 for every other.</param>
/// <param name="assembly">The one assembly the name came from, or null where several or none did.</param>
/// <param name="tag">The tag the host draws the row's glyph from.</param>
internal readonly struct Offer(string insert, OfferShape shape, int rank, string? assembly, string tag)
{
    /// <summary>The text a commit writes into the segment, generic type parameters included.</summary>
    internal string Insert { get; } = insert;

    /// <summary>Which shape the name takes in the list.</summary>
    internal OfferShape Shape { get; } = shape;

    /// <summary>
    ///     Where the name sorts: 0 for the referenced projects' own names and the lone star, 1 for everything
    ///     else, which is the whole of how this provider puts the product first.
    /// </summary>
    internal int Rank { get; } = rank;

    /// <summary>The assembly shown beside the row, or null where the row names no single one.</summary>
    internal string? Assembly { get; } = assembly;

    /// <summary>The tag the host draws the row's glyph from.</summary>
    internal string Tag { get; } = tag;

    internal static Offer NamespaceStep(string name, bool product, string? assembly)
    {
        return new Offer(name, OfferShape.NamespaceStep, product ? 0 : 1, assembly, WellKnownTags.Namespace);
    }

    internal static Offer Subtree(string name, bool product)
    {
        return new Offer(name + ".*", OfferShape.Subtree, product ? 0 : 1, null, WellKnownTags.Namespace);
    }

    internal static Offer LoneStar()
    {
        return new Offer("*", OfferShape.LoneStar, 0, null, WellKnownTags.Namespace);
    }

    internal static Offer Type(string leaf, bool product, string? assembly, string tag)
    {
        return new Offer(leaf, OfferShape.Type, product ? 0 : 1, assembly, tag);
    }

    internal static Offer SimpleName(string name, string tag)
    {
        return new Offer(name, OfferShape.SimpleName, 0, null, tag);
    }
}
