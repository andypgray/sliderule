namespace SlideRule.Codebase;

/// <summary>
///     A type in the extracted model. It implements <see cref="ITypeInfo" />, so the facts a <c>Where</c>
///     predicate in a spec sees and the facts here are one surface.
/// </summary>
/// <remarks>
///     Compare types by reference and never by name: where a project declares a name a referenced assembly
///     also supplies, two types wear it, told apart by <see cref="ProjectName" /> and <see cref="IsExternal" />.
/// </remarks>
// Minted shallow (identity only) and filled through the internal setters during model construction, which
// is what tolerates self-referential shapes such as `[Foo] class FooAttribute`, where a type is its own
// attribute. FullName is symbol.OriginalDefinition.ToDisplayString().
internal sealed class TypeNode : ITypeInfo
{
    internal TypeNode(
        string fullName, string symbolId, string name, string @namespace, TypeKind kind,
        Accessibility accessibility, bool isSealed, bool isStatic, bool isAbstract, bool isRecord,
        bool isGenerated, string projectName, bool isExternal)
    {
        FullName = fullName;
        SymbolId = symbolId;
        Name = name;
        Namespace = @namespace;
        Kind = kind;
        Accessibility = accessibility;
        IsSealed = isSealed;
        IsStatic = isStatic;
        IsAbstract = isAbstract;
        IsRecord = isRecord;
        IsGenerated = isGenerated;
        ProjectName = projectName;
        IsExternal = isExternal;
        DeclarationSites = Array.Empty<SourceLocation>();
        FilePaths = Array.Empty<string>();
        AlsoDeclaredBy = Array.Empty<string>();
        Interfaces = Array.Empty<ITypeInfo>();
        Attributes = Array.Empty<ITypeInfo>();
        AllInterfaces = Array.Empty<TypeConstruction>();
        BaseTypeChain = Array.Empty<TypeConstruction>();
        AttributeConstructions = Array.Empty<TypeConstruction>();
        Members = Array.Empty<MemberNode>();
    }

    /// <summary>
    ///     Gets the fully-qualified name: namespace-qualified, without <c>global::</c>, a nested type dotted
    ///     onto its container, an open generic carrying its declared type-parameter names —
    ///     <c>MyApp.Web.IHandler&lt;T&gt;</c>, <c>MyApp.Domain.Order.Line</c>. Always the definition's name; a
    ///     constructed generic is never named here.
    /// </summary>
    public string FullName { get; }

    /// <summary>
    ///     Gets the identity a baseline records for this type: its Roslyn documentation comment ID in the
    ///     <c>T:</c> form — <c>T:MyApp.Web.HomeController</c>, <c>T:MyApp.Web.IHandler`1</c> for an open
    ///     generic, <c>T:MyApp.Domain.Order.Line</c> for a nested type — or <c>unresolved:{FullName}</c> where
    ///     the compiler gives the type no such ID. Two types sharing a <see cref="FullName" /> share this ID as
    ///     well, so a baseline entry written for either covers the other.
    /// </summary>
    public string SymbolId { get; }

    /// <summary>
    ///     Gets whether no compilation in the solution declares this type — a framework or package type the
    ///     model carries so that a rule can name it. An external type has no declaration sites, file paths,
    ///     hierarchy, attributes or members: those lists are all empty and <see cref="BaseType" /> is null. Its
    ///     shape facts (<see cref="Kind" />, <see cref="Accessibility" /> and the modifier flags) are read from
    ///     the referenced assembly and are accurate.
    /// </summary>
    public bool IsExternal { get; }

    /// <summary>
    ///     Gets where the type is declared, one entry per part of a partial type, ordered by file then line.
    /// </summary>
    public IReadOnlyList<SourceLocation> DeclarationSites { get; internal set; }

    /// <summary>
    ///     Gets the other projects declaring this same fully-qualified name, ordered ordinal — one source file
    ///     compiled into several projects, by a linked <c>&lt;Compile Include&gt;</c> or by shared source, or a
    ///     copy of its own in each, such as a polyfill. Empty for an ordinary type.
    /// </summary>
    /// <remarks>
    ///     <see cref="ProjectName" /> is the first declarer and the facts here are that declarer's, but a rule
    ///     naming any project in the list still reaches this type (see <see cref="IsDeclaredBy" />), because
    ///     each of them compiles the type itself rather than referencing the first. A reference into the type
    ///     counts against the copy it reached: a declarer's own copy for that declarer, and otherwise the copy
    ///     of the declarer the referencing project references. A reference out of it counts for each declarer
    ///     whose own copy makes it, so where the copies differ, one copy's references are not the others'.
    /// </remarks>
    public IReadOnlyList<string> AlsoDeclaredBy { get; internal set; }

    // Each declaring project's own declaration sites, keyed by project name, set beside AlsoDeclaredBy during model
    // construction and non-null exactly when that roster is non-empty. DeclarationSites stays their union; these
    // let a project-headed selection count and place by its own copy (GRAMMAR §4.1, §6).
    internal IReadOnlyDictionary<string, IReadOnlyList<SourceLocation>>? DeclarationSitesByDeclarer { get; set; }

    /// <summary>
    ///     Gets every interface the type implements, directly or through a base class or interface inheritance,
    ///     with type arguments substituted and ordered ordinal by <see cref="TypeConstruction.FullName" />. A
    ///     declared type's list runs on through the assemblies it references, so it includes external interfaces.
    /// </summary>
    public IReadOnlyList<TypeConstruction> AllInterfaces { get; internal set; }

    /// <summary>
    ///     Gets the base types in derivation order, nearest first, ending at <c>System.Object</c>; the order is
    ///     meaningful, so this list is not sorted. Empty for an interface. A declared type's chain runs on through
    ///     the assemblies it references, so it includes external base types.
    /// </summary>
    public IReadOnlyList<TypeConstruction> BaseTypeChain { get; internal set; }

    /// <summary>
    ///     Gets the attributes written on the type's own declaration, ordered ordinal by
    ///     <see cref="TypeConstruction.FullName" />; attributes inherited from a base type are not included. A
    ///     generic attribute is recorded like any other, keeping both its definition and its construction.
    /// </summary>
    public IReadOnlyList<TypeConstruction> AttributeConstructions { get; internal set; }

    /// <summary>
    ///     Gets the type's declared methods, properties, fields and events, ordered ordinal by
    ///     <see cref="MemberNode.SymbolId" />. Each member's <see cref="MemberNode.DeclaringType" /> is this same
    ///     instance. Constructors, accessors, operators, conversions, finalizers, indexers, explicit interface
    ///     implementations and compiler-generated members are all left out, and an enum or a delegate
    ///     contributes none.
    /// </summary>
    public IReadOnlyList<MemberNode> Members { get; internal set; }

    /// <inheritdoc />
    public IReadOnlyList<string> FilePaths { get; internal set; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Namespace { get; }

    /// <inheritdoc />
    public TypeKind Kind { get; }

    /// <inheritdoc />
    public string ProjectName { get; }

    /// <inheritdoc />
    public Accessibility Accessibility { get; }

    /// <inheritdoc />
    public bool IsSealed { get; }

    /// <inheritdoc />
    public bool IsStatic { get; }

    /// <inheritdoc />
    public bool IsAbstract { get; }

    /// <inheritdoc />
    public bool IsRecord { get; }

    /// <inheritdoc />
    public bool IsGenerated { get; }

    /// <inheritdoc />
    public ITypeInfo? BaseType { get; internal set; }

    /// <inheritdoc />
    public IReadOnlyList<ITypeInfo> Interfaces { get; internal set; }

    /// <inheritdoc />
    public IReadOnlyList<ITypeInfo> Attributes { get; internal set; }

    /// <summary>
    ///     Reports whether <paramref name="projectName" />, as <see cref="ProjectNode.Name" /> reports it, declares
    ///     this type: <see cref="ProjectName" /> or any entry of <see cref="AlsoDeclaredBy" />, compared ordinal.
    /// </summary>
    // Walked rather than cached, because AlsoDeclaredBy is stamped during model construction, after the node
    // is minted — a roster built at mint time would be stale for every conflated type. The list is empty for
    // every ordinary type, so the walk is one comparison.
    public bool IsDeclaredBy(string projectName)
    {
        if (string.Equals(ProjectName, projectName, StringComparison.Ordinal)) return true;

        IReadOnlyList<string> alsoDeclaredBy = AlsoDeclaredBy;
        for (var i = 0; i < alsoDeclaredBy.Count; i++)
            if (string.Equals(alsoDeclaredBy[i], projectName, StringComparison.Ordinal))
                return true;

        return false;
    }

    // Every project declaring this type, the winner first — the roster a rule naming a project tests against.
    // IsDeclaredBy stays a walk rather than a read of this, because it runs per node per rule.
    internal IEnumerable<string> Declarers
    {
        get
        {
            yield return ProjectName;

            foreach (string declarer in AlsoDeclaredBy)
                yield return declarer;
        }
    }

    // The declaration sites one declaring project compiles: its own share where several projects declare the type,
    // and nothing for a project that does not declare it. A node whose shares were never recorded answers with the
    // union for every declarer, which is the attribution-blind reading rather than a card silently skipped.
    internal IReadOnlyList<SourceLocation> DeclarationSitesOf(string projectName)
    {
        if (DeclarationSitesByDeclarer is null)
            return IsDeclaredBy(projectName) ? DeclarationSites : Array.Empty<SourceLocation>();

        return DeclarationSitesByDeclarer.TryGetValue(projectName, out IReadOnlyList<SourceLocation>? own)
            ? own
            : Array.Empty<SourceLocation>();
    }
}
