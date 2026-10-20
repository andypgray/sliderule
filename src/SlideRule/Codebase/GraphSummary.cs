namespace SlideRule.Codebase;

// The pre-spec codebase survey and its parts, clustered in one file: they are one cohesive summary shape
// produced together, not independent product types. Every list is ordinal-ordered for reproducibility.

/// <summary>
///     A survey of an extracted codebase, needing no architecture spec. Grouped counts only: no list of sites
///     appears here, and the sites arrive from <c>check</c> once a rule names them.
/// </summary>
internal sealed class GraphSummary
{
    internal GraphSummary(
        IReadOnlyList<ProjectSummary> projects,
        IReadOnlyList<ProjectEdgeSummary> projectEdges,
        IReadOnlyList<ExternalEdgeSummary> externalEdges,
        IReadOnlyList<MultiplyDeclaredTypeSummary> multiplyDeclaredTypes,
        IReadOnlyList<ShadowedTypeSummary> shadowedTypes)
    {
        Projects = projects;
        ProjectEdges = projectEdges;
        ExternalEdges = externalEdges;
        MultiplyDeclaredTypes = multiplyDeclaredTypes;
        ShadowedTypes = shadowedTypes;
    }

    /// <summary>
    ///     Gets the surveyed projects, ordered by name (ordinal).
    /// </summary>
    public IReadOnlyList<ProjectSummary> Projects { get; }

    /// <summary>
    ///     Gets the reference edges observed between projects, ordered by (source, target) (ordinal). These are the
    ///     references the code actually makes, which is not the same list as the references the projects declare.
    /// </summary>
    public IReadOnlyList<ProjectEdgeSummary> ProjectEdges { get; }

    /// <summary>
    ///     Gets the references out to types no project of the solution declares, grouped by namespace root and ordered
    ///     by (source, root) (ordinal).
    /// </summary>
    public IReadOnlyList<ExternalEdgeSummary> ExternalEdges { get; }

    /// <summary>
    ///     Gets the types more than one project declares, ordered by full name (ordinal). Empty in the common case, and
    ///     reading it is how you know whether the survey's per-project figures can be taken at face value.
    /// </summary>
    public IReadOnlyList<MultiplyDeclaredTypeSummary> MultiplyDeclaredTypes { get; }

    /// <summary>
    ///     Gets the full names a project declares that a referenced assembly also supplies, ordered by full name
    ///     (ordinal). Empty in the common case, and the one place in the survey where a name does not identify a single
    ///     type.
    /// </summary>
    public IReadOnlyList<ShadowedTypeSummary> ShadowedTypes { get; }
}

/// <summary>
///     One project in the survey: what the model records about the project, and how many types it
///     declares in each namespace. A member that shares its name with a <see cref="ProjectNode" /> member
///     means what that member means. Comparing <see cref="ProjectReferences" /> against
///     <see cref="GraphSummary.ProjectEdges" /> is how a reference that is declared but never used shows
///     up.
/// </summary>
internal sealed class ProjectSummary
{
    internal ProjectSummary(
        string name,
        IReadOnlyList<string> projectReferences,
        int types,
        int generated,
        IReadOnlyList<NamespaceCount> namespaces,
        bool? solutionMember = null,
        IReadOnlyList<string>? targetFrameworks = null,
        string? factsFollow = null,
        IReadOnlyList<PackageReference>? packageReferences = null,
        bool? isPackable = null,
        bool? locksPackages = null,
        IReadOnlyList<string>? undeclaredProjectReferences = null)
    {
        Name = name;
        ProjectReferences = projectReferences;
        UndeclaredProjectReferences = undeclaredProjectReferences ?? [];
        Types = types;
        Generated = generated;
        Namespaces = namespaces;
        SolutionMember = solutionMember;
        TargetFrameworks = targetFrameworks ?? [];
        FactsFollow = factsFollow;
        PackageReferences = packageReferences ?? [];
        IsPackable = isPackable;
        LocksPackages = locksPackages;
    }

    public string Name { get; }

    public IReadOnlyList<string> ProjectReferences { get; }

    public IReadOnlyList<string> UndeclaredProjectReferences { get; }

    /// <summary>
    ///     Gets the count of this project's solution-declared (non-external) types.
    /// </summary>
    public int Types { get; }

    /// <summary>
    ///     Gets how many of <see cref="Types" /> a generator emitted (<see cref="ITypeInfo.IsGenerated" />), a subset
    ///     of that count, never a separate population. A project whose two counts are close is one where
    ///     <c>arch.Project(…)</c> would aim most of a rule at code nobody can edit, and <c>Authored()</c> is the
    ///     narrowing that leaves it out.
    /// </summary>
    public int Generated { get; }

    /// <summary>
    ///     Gets the distinct namespaces of this project's declared types with a count for each, ordinal by namespace.
    /// </summary>
    public IReadOnlyList<NamespaceCount> Namespaces { get; }

    /// <summary>
    ///     Gets whether the solution declares this project. A project the solution does not declare is reported
    ///     rather than filtered out, because the survey is where it should be noticed.
    /// </summary>
    public bool? SolutionMember { get; }

    public IReadOnlyList<string> TargetFrameworks { get; }

    public string? FactsFollow { get; }

    public IReadOnlyList<PackageReference> PackageReferences { get; }

    public bool? IsPackable { get; }

    public bool? LocksPackages { get; }
}

/// <summary>A namespace and how many of a project's declared types reside in it.</summary>
internal sealed class NamespaceCount
{
    internal NamespaceCount(string @namespace, int types, int generated)
    {
        Namespace = @namespace;
        Types = types;
        Generated = generated;
    }

    /// <summary>
    ///     Gets the namespace; the global namespace renders as <c>(global)</c>.
    /// </summary>
    public string Namespace { get; }

    public int Types { get; }

    /// <summary>
    ///     Gets how many of <see cref="Types" /> a generator emitted — a subset of that count. A namespace where the
    ///     two are equal is wholly generator output, which is the shape that must never become a layer glob: a compiled
    ///     view tier collects under one namespace nobody typed.
    /// </summary>
    public int Generated { get; }
}

/// <summary>
///     A reference edge observed from one project into another, with the number of distinct type pairs
///     behind it. References within a single project are left out; the survey is about the crossings
///     between projects, which is what a layering or boundary rule is written about.
/// </summary>
internal sealed class ProjectEdgeSummary
{
    internal ProjectEdgeSummary(string source, string target, int references)
    {
        Source = source;
        Target = target;
        References = references;
    }

    public string Source { get; }

    /// <summary>
    ///     Gets the referenced project (never external).
    /// </summary>
    public string Target { get; }

    public int References { get; }
}

/// <summary>
///     One type that several projects declare, as <see cref="TypeNode.AlsoDeclaredBy" /> records it: what to read
///     before writing a rule about a project, since <c>arch.Project</c> named on any declarer selects the type
///     while its members and hierarchy answer from <see cref="FactsFollow" />'s compilation alone.
/// </summary>
internal sealed class MultiplyDeclaredTypeSummary
{
    internal MultiplyDeclaredTypeSummary(string type, IReadOnlyList<string> declaredBy, string factsFollow)
    {
        Type = type;
        DeclaredBy = declaredBy;
        FactsFollow = factsFollow;
    }

    /// <summary>
    ///     Gets the type's fully-qualified name.
    /// </summary>
    public string Type { get; }

    /// <summary>
    ///     Gets every project that declares it, ordinal-ordered, <see cref="FactsFollow" /> among them: the whole
    ///     roster, not only the declarers whose facts were displaced.
    /// </summary>
    public IReadOnlyList<string> DeclaredBy { get; }

    /// <summary>Gets the declarer whose facts and project attribution the type carries: the first one.</summary>
    public string FactsFollow { get; }
}

/// <summary>
///     The survey's copy of a <see cref="ShadowedName" />, to read before writing a rule about that name. A
///     member means what the <see cref="ShadowedName" /> member of the same name means.
/// </summary>
internal sealed class ShadowedTypeSummary
{
    internal ShadowedTypeSummary(
        string type, string declaredBy, IReadOnlyList<string> suppliedBy, IReadOnlyList<string> boundFromAssemblyBy)
    {
        Type = type;
        DeclaredBy = declaredBy;
        SuppliedBy = suppliedBy;
        BoundFromAssemblyBy = boundFromAssemblyBy;
    }

    /// <summary>Gets the shared fully-qualified name, <see cref="ShadowedName.FullName" />.</summary>
    public string Type { get; }

    public string DeclaredBy { get; }

    public IReadOnlyList<string> SuppliedBy { get; }

    public IReadOnlyList<string> BoundFromAssemblyBy { get; }
}

/// <summary>
///     The references from one project out to types under one external namespace root, with the number of
///     distinct type pairs behind them. Grouping this way is what lets a large framework surface review as a
///     handful of roots instead of a per-type list.
/// </summary>
internal sealed class ExternalEdgeSummary
{
    internal ExternalEdgeSummary(string source, string targetNamespaceRoot, int references)
    {
        Source = source;
        TargetNamespaceRoot = targetNamespaceRoot;
        References = references;
    }

    public string Source { get; }

    /// <summary>
    ///     Gets the first two dot-segments of the external target's namespace, such as <c>System.Data</c>; a
    ///     one-segment namespace gives that segment, and the global namespace gives <c>(global)</c>.
    /// </summary>
    public string TargetNamespaceRoot { get; }

    public int References { get; }
}
