namespace SlideRule.Codebase;

/// <summary>
///     The extracted codebase a check runs against. Every list is read-only and ordered ordinal, so two runs
///     over unchanged source produce the same lists in the same order; each property names the keys it is
///     sorted by.
/// </summary>
/// <remarks>
///     The model is one object graph: the types at the ends of an edge, a member's declaring type and a
///     <see cref="MemberReference" />'s containing type are the very instances <see cref="Types" /> lists. No
///     edge runs from a type to itself.
/// </remarks>
internal sealed class CodebaseModel
{
    internal CodebaseModel(
        IReadOnlyList<TypeNode> types,
        IReadOnlyList<ReferenceEdge> edges,
        IReadOnlyList<MemberEdge> memberEdges,
        IReadOnlyList<ConstructorEdge> constructorEdges,
        IReadOnlyList<InjectionEdge> injectionEdges,
        IReadOnlyList<CatchEdge> catchEdges,
        IReadOnlyList<ThrowEdge> throwEdges,
        IReadOnlyList<ExposureEdge> exposureEdges,
        IReadOnlyList<ServiceRegistration> serviceRegistrations,
        IReadOnlyList<ProjectNode> projects,
        IReadOnlyList<ShadowedName> shadowedNames,
        IReadOnlyList<string> mergeNotes)
    {
        Types = types;
        Edges = edges;
        MemberEdges = memberEdges;
        ConstructorEdges = constructorEdges;
        InjectionEdges = injectionEdges;
        CatchEdges = catchEdges;
        ThrowEdges = throwEdges;
        ExposureEdges = exposureEdges;
        ServiceRegistrations = serviceRegistrations;
        Projects = projects;
        ShadowedNames = shadowedNames;
        MergeNotes = mergeNotes;
    }

    /// <summary>
    ///     Gets every type in the model: the ones the solution's projects declare, and a shallow entry for each
    ///     external type they reference. Ordered by <see cref="TypeNode.FullName" />, declarations before external
    ///     entries, then by <see cref="TypeNode.ProjectName" />.
    /// </summary>
    // The (declaration-before-external, project) tie-break is what makes this a total order. FullName alone
    // is not one, because a shadowed name carries two nodes, and without the tie-break every rendered
    // document would take dictionary enumeration order and lose its byte-stability.
    public IReadOnlyList<TypeNode> Types { get; }

    /// <summary>
    ///     Gets the type-to-type reference edges: one entry per (source type, target type) pair, carrying every site
    ///     that produced it. Ordered by (source full name, target full name).
    /// </summary>
    public IReadOnlyList<ReferenceEdge> Edges { get; }

    /// <summary>
    ///     Gets the member uses: one entry per (source type, used member) pair, carrying every site. Ordered by
    ///     (source full name, member <see cref="MemberReference.SymbolId" />). Recorded beside <see cref="Edges" />
    ///     rather than instead of it, so a member use also appears there as a reference edge to the member's
    ///     containing type.
    /// </summary>
    public IReadOnlyList<MemberEdge> MemberEdges { get; }

    /// <summary>
    ///     Gets the object creations: one entry per (source type, constructed type) pair, carrying every site.
    ///     Ordered by (source full name, constructed full name). Recorded beside <see cref="Edges" /> rather than
    ///     instead of it, so a creation also appears there as a reference edge to the constructed type.
    /// </summary>
    public IReadOnlyList<ConstructorEdge> ConstructorEdges { get; }

    /// <summary>
    ///     Gets the constructor injections: one entry per (source type, injected parameter type) pair, carrying every
    ///     site. Ordered by (source full name, injected full name). Recorded beside <see cref="Edges" /> rather than
    ///     instead of it, so an injected parameter type also appears there as a reference edge.
    /// </summary>
    public IReadOnlyList<InjectionEdge> InjectionEdges { get; }

    /// <summary>
    ///     Gets the caught exception types: one entry per (source type, caught type) pair, carrying every site.
    ///     Ordered by (source full name, caught full name). A typed catch also appears in <see cref="Edges" />, its
    ///     type name being a reference like any other; a bare catch names no type and appears there not at all.
    /// </summary>
    public IReadOnlyList<CatchEdge> CatchEdges { get; }

    /// <summary>
    ///     Gets the thrown exception types: one entry per (source type, thrown type) pair, carrying every site.
    ///     Ordered by (source full name, thrown full name). A <c>throw new X()</c> appears here, in
    ///     <see cref="ConstructorEdges" /> and in <see cref="Edges" /> alike.
    /// </summary>
    public IReadOnlyList<ThrowEdge> ThrowEdges { get; }

    /// <summary>
    ///     Gets the types named in public signature positions: one entry per (source type, exposed type) pair,
    ///     carrying every site. Ordered by (source full name, exposed full name). Recorded beside <see cref="Edges" />
    ///     rather than instead of it, so a type the signature spells also appears there as a reference edge.
    /// </summary>
    public IReadOnlyList<ExposureEdge> ExposureEdges { get; }

    /// <summary>
    ///     Gets the container registrations the source spells: one entry per (lifetime, service type, implementation
    ///     type) fact, ordered by those three.
    /// </summary>
    public IReadOnlyList<ServiceRegistration> ServiceRegistrations { get; }

    /// <summary>
    ///     Gets every project in the model, ordered by name (ordinal).
    /// </summary>
    public IReadOnlyList<ProjectNode> Projects { get; }

    /// <summary>
    ///     Gets every full name a project declares that a referenced assembly also supplies, ordered by that name
    ///     (ordinal), and empty in the common case: the one place a name in <see cref="Types" /> does not identify a
    ///     single type.
    /// </summary>
    public IReadOnlyList<ShadowedName> ShadowedNames { get; }

    /// <summary>
    ///     Gets advisory notes about how this model was assembled, as prose to display and not to parse:
    ///     <see cref="ShadowedNames" /> and <see cref="TypeNode.AlsoDeclaredBy" /> carry the same facts in a form to
    ///     act on. Empty in the common case. A note never means a failed load and never fails a check: it warns
    ///     that an attribution may surprise a reader.
    /// </summary>
    /// <remarks>
    ///     Three things get a note: the types several projects declare, one note per set of declaring projects
    ///     naming every type the set shares; a project that compiles once per target framework and has a type
    ///     more than one of those frameworks declares, one note per project naming its frameworks and each
    ///     framework a shared type took its facts from, the first of them being
    ///     <see cref="ProjectNode.FactsFollow" />; and a name a referenced assembly also supplies, one note per
    ///     declaring project. The project-level notes come first, each ordinal by project name, then the notes
    ///     about the types several projects declare, ordinal by the first declarer and then by the rest.
    /// </remarks>
    public IReadOnlyList<string> MergeNotes { get; }
}
