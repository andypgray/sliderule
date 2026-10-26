using SlideRule.Codebase;

namespace SlideRule.Tests.Extraction;

/// <summary>Query helpers over a <see cref="CodebaseModel" /> to keep extraction assertions terse.</summary>
internal static class ModelQuery
{
    /// <summary>The lines of a list of sites, in recorded order: <c>edge.Sites.Lines()</c>.</summary>
    /// <remarks>
    ///     On the sites rather than on each edge: the eight edge kinds share no sited contract in the shipped
    ///     model, and naming the list at the call site says which of an edge's site lists is meant.
    /// </remarks>
    public static IReadOnlyList<int> Lines(this IEnumerable<SourceLocation> sites)
    {
        return sites.Select(site => site.Line)
            .ToList();
    }

    /// <summary>The files of a list of sites, in recorded order.</summary>
    public static IReadOnlyList<string> Files(this IEnumerable<SourceLocation> sites)
    {
        return sites.Select(site => site.FilePath)
            .ToList();
    }

    /// <summary>The sites as <c>file:line</c> strings, the form a site prints as, in recorded order.</summary>
    public static IReadOnlyList<string> Rendered(this IEnumerable<SourceLocation> sites)
    {
        return sites.Select(site => site.ToString())
            .ToList();
    }

    public static TypeNode Type(this CodebaseModel model, string fullName)
    {
        return ExactlyOne(model.Types, type => type.FullName == fullName, $"the type {fullName}", Endpoint);
    }

    public static ProjectNode Project(this CodebaseModel model, string name)
    {
        return ExactlyOne(
            model.Projects,
            project => project.Name == name,
            $"the project {name}",
            project => project.Name);
    }

    public static ReferenceEdge Edge(this CodebaseModel model, string sourceFullName, string targetFullName)
    {
        return Between(model.Edges, sourceFullName, targetFullName, "reference edge");
    }

    public static bool HasEdge(this CodebaseModel model, string sourceFullName, string targetFullName)
    {
        return AnyBetween(model.Edges, sourceFullName, targetFullName);
    }

    public static MemberEdge MemberEdge(this CodebaseModel model, string sourceFullName, string memberSymbolId)
    {
        IReadOnlyList<MemberEdge> fromSource = model.MemberEdges(sourceFullName);
        return ExactlyOne(
            fromSource,
            edge => edge.Member.SymbolId == memberSymbolId,
            $"the member edge {sourceFullName} -> {memberSymbolId}",
            edge => edge.Member.SymbolId);
    }

    public static IReadOnlyList<MemberEdge> MemberEdges(this CodebaseModel model, string sourceFullName)
    {
        return model.MemberEdges.Where(e => e.Source.FullName == sourceFullName)
            .ToList();
    }

    // ── construction edges (GRAMMAR §4.5) ─────────────────────────────────────────────────────────────────

    public static ConstructorEdge ConstructorEdge(this CodebaseModel model, string sourceFullName, string constructedFullName)
    {
        return Between(model.ConstructorEdges, sourceFullName, constructedFullName, "constructor edge");
    }

    public static IReadOnlyList<ConstructorEdge> ConstructorEdges(this CodebaseModel model, string sourceFullName)
    {
        return From(model.ConstructorEdges, sourceFullName);
    }

    public static bool HasConstructorEdge(this CodebaseModel model, string sourceFullName, string constructedFullName)
    {
        return AnyBetween(model.ConstructorEdges, sourceFullName, constructedFullName);
    }

    public static string FullName(this ITypeInfo type)
    {
        return ((TypeNode)type).FullName;
    }

    // ── injection edges (GRAMMAR §4.7) ────────────────────────────────────────────────────────────────────

    public static InjectionEdge InjectionEdge(this CodebaseModel model, string sourceFullName, string injectedFullName)
    {
        return Between(model.InjectionEdges, sourceFullName, injectedFullName, "injection edge");
    }

    public static IReadOnlyList<InjectionEdge> InjectionEdges(this CodebaseModel model, string sourceFullName)
    {
        return From(model.InjectionEdges, sourceFullName);
    }

    public static bool HasInjectionEdge(this CodebaseModel model, string sourceFullName, string injectedFullName)
    {
        return AnyBetween(model.InjectionEdges, sourceFullName, injectedFullName);
    }

    // ── catch edges (GRAMMAR §4.8) ────────────────────────────────────────────────────────────────────────

    public static CatchEdge CatchEdge(this CodebaseModel model, string sourceFullName, string caughtFullName)
    {
        return Between(model.CatchEdges, sourceFullName, caughtFullName, "catch edge");
    }

    public static IReadOnlyList<CatchEdge> CatchEdges(this CodebaseModel model, string sourceFullName)
    {
        return From(model.CatchEdges, sourceFullName);
    }

    public static bool HasCatchEdge(this CodebaseModel model, string sourceFullName, string caughtFullName)
    {
        return AnyBetween(model.CatchEdges, sourceFullName, caughtFullName);
    }

    // ── throw edges (GRAMMAR §4.8) ────────────────────────────────────────────────────────────────────────

    public static ThrowEdge ThrowEdge(this CodebaseModel model, string sourceFullName, string thrownFullName)
    {
        return Between(model.ThrowEdges, sourceFullName, thrownFullName, "throw edge");
    }

    public static IReadOnlyList<ThrowEdge> ThrowEdges(this CodebaseModel model, string sourceFullName)
    {
        return From(model.ThrowEdges, sourceFullName);
    }

    public static bool HasThrowEdge(this CodebaseModel model, string sourceFullName, string thrownFullName)
    {
        return AnyBetween(model.ThrowEdges, sourceFullName, thrownFullName);
    }

    // ── exposure edges (GRAMMAR §4.9) ─────────────────────────────────────────────────────────────────────

    public static ExposureEdge ExposureEdge(this CodebaseModel model, string sourceFullName, string exposedFullName)
    {
        return Between(model.ExposureEdges, sourceFullName, exposedFullName, "exposure edge");
    }

    public static IReadOnlyList<ExposureEdge> ExposureEdges(this CodebaseModel model, string sourceFullName)
    {
        return From(model.ExposureEdges, sourceFullName);
    }

    public static bool HasExposureEdge(this CodebaseModel model, string sourceFullName, string exposedFullName)
    {
        return AnyBetween(model.ExposureEdges, sourceFullName, exposedFullName);
    }

    // ── registration facts (GRAMMAR §4.7) ─────────────────────────────────────────────────────────────────

    public static ServiceRegistration Registration(
        this CodebaseModel model, Lifetime lifetime, string serviceFullName, string? implementationFullName)
    {
        var sought = $"the registration {RegistrationText(lifetime, serviceFullName, implementationFullName)}";
        return ExactlyOne(
            model.ServiceRegistrations,
            registration => registration.Lifetime == lifetime
                            && registration.ServiceFullName == serviceFullName
                            && registration.ImplementationFullName == implementationFullName,
            sought,
            registration => RegistrationText(
                registration.Lifetime, registration.ServiceFullName, registration.ImplementationFullName));
    }

    // ── declared members (GRAMMAR §4.6) ───────────────────────────────────────────────────────────────────

    public static MemberNode Member(this TypeNode type, string symbolId)
    {
        return ExactlyOne(
            type.Members,
            member => member.SymbolId == symbolId,
            $"the member {symbolId} of {type.FullName}",
            member => member.SymbolId);
    }

    public static MemberNode MemberNamed(this TypeNode type, string name)
    {
        return ExactlyOne(
            type.Members,
            member => member.Name == name,
            $"the member named {name} on {type.FullName}",
            member => member.SymbolId);
    }

    public static MemberNode Member(this CodebaseModel model, string typeFullName, string symbolId)
    {
        return model.Type(typeFullName)
            .Member(symbolId);
    }

    /// <summary>The type's declared-member SymbolIds, in the model's ordinal-by-SymbolId order.</summary>
    public static IReadOnlyList<string> MemberIds(this TypeNode type)
    {
        return type.Members.Select(m => m.SymbolId)
            .ToList();
    }

    /// <summary>
    ///     The member's declared attributes as (definition, constructed) name pairs, in recorded order — so a
    ///     generic attribute's divergence is assertable as one value.
    /// </summary>
    public static IReadOnlyList<(string Definition, string Constructed)> AttributeNames(this MemberNode member)
    {
        return member.Attributes.Select(a => (a.DefinitionFullName, a.FullName))
            .ToList();
    }

    /// <summary>The full names of <paramref name="types" />, in the order given.</summary>
    public static IReadOnlyList<string> FullNames(this IEnumerable<TypeNode> types)
    {
        return types.Select(type => type.FullName)
            .ToList();
    }

    /// <summary>The full names of <paramref name="types" />, ordinally sorted.</summary>
    public static IReadOnlyList<string> SortedFullNames(this IEnumerable<TypeNode> types)
    {
        return types.Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The far-end full names of <paramref name="edges" />, in the order given.</summary>
    public static IReadOnlyList<string> TargetNames<TEdge>(this IEnumerable<TEdge> edges)
        where TEdge : IEdgeEnds
    {
        return edges.Select(edge => edge.Target.FullName)
            .ToList();
    }

    // A miss lists what was searched and a duplicate every match, either cut to this many, so a red over a
    // whole-solution model stays readable.
    private const int ListedEntries = 20;

    // Exactly one candidate matches, or the throw names what was sought and what was there. It throws what LINQ's
    // Single throws, so the getters stay queries rather than assertions.
    private static T ExactlyOne<T>(
        IReadOnlyCollection<T> candidates, Func<T, bool> match, string sought, Func<T, string> describe)
    {
        List<T> matching = candidates.Where(match)
            .ToList();
        if (matching.Count == 1) return matching[0];

        List<T> listed = matching.Count == 0 ? candidates.ToList() : matching;
        IEnumerable<string> shown = listed.Take(ListedEntries)
            .Select(describe);
        string entries = string.Join(", ", shown);
        string rest = listed.Count > ListedEntries ? $", and {listed.Count - ListedEntries} more" : "";
        string found = matching.Count == 0 ? "none among" : $"{matching.Count}:";
        throw new InvalidOperationException($"Looking for {sought}, found {found} [{entries}{rest}].");
    }

    // Every type-ended family answers IEdgeEnds.Target with its own far end, so this one lookup serves them all;
    // a miss lists the far ends the source does reach.
    private static TEdge Between<TEdge>(
        IEnumerable<TEdge> edges, string sourceFullName, string targetFullName, string noun)
        where TEdge : IEdgeEnds
    {
        IReadOnlyList<TEdge> fromSource = From(edges, sourceFullName);
        return ExactlyOne(
            fromSource,
            edge => edge.Target.FullName == targetFullName,
            $"the {noun} {sourceFullName} -> {targetFullName}",
            edge => Endpoint(edge.Target));
    }

    private static bool AnyBetween<TEdge>(IEnumerable<TEdge> edges, string sourceFullName, string targetFullName)
        where TEdge : IEdgeEnds
    {
        return edges.Any(edge => edge.Source.FullName == sourceFullName && edge.Target.FullName == targetFullName);
    }

    private static IReadOnlyList<TEdge> From<TEdge>(IEnumerable<TEdge> edges, string sourceFullName)
        where TEdge : IEdgeEnds
    {
        return edges.Where(edge => edge.Source.FullName == sourceFullName)
            .ToList();
    }

    // A node as ModelDump renders an endpoint, so two nodes wearing one name read apart.
    private static string Endpoint(TypeNode node)
    {
        return $"{node.FullName}@{node.ProjectName}";
    }

    private static string RegistrationText(Lifetime lifetime, string serviceFullName, string? implementationFullName)
    {
        return $"{lifetime} {serviceFullName} -> {implementationFullName ?? "<none>"}";
    }
}
