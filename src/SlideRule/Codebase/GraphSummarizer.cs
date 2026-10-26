using SlideRule.Internal;

namespace SlideRule.Codebase;

/// <summary>
///     Turns a <see cref="CodebaseModel" /> into a <see cref="GraphSummary" />. Pure counting over a model
///     that is already ordered, with no I/O and no compiler work, and every list it produces is
///     ordinal-ordered, so one model always summarizes to the same document.
/// </summary>
internal static class GraphSummarizer
{
    private const string GlobalNamespaceLabel = "(global)";

    /// <summary>
    ///     Builds the survey over every project in <paramref name="model" />. Its project edges and external
    ///     references are the references the code actually makes, each counted at the project whose compilation
    ///     made it, by the rules <see cref="TypeNode.AlsoDeclaredBy" /> states for a name several projects declare.
    /// </summary>
    public static GraphSummary Summarize(CodebaseModel model)
    {
        // One pass over the type universe rather than one per project: a per-project scan makes the survey
        // O(projects x types), and orienting on a large unfamiliar solution is the whole job. The lookup
        // preserves Types order within each group, and a project that declares nothing gets an empty group.
        ILookup<string, TypeNode> declaredByProject = model.Types
            .Where(type => !type.IsExternal)
            .ToLookup(type => type.ProjectName, StringComparer.Ordinal);

        List<ProjectSummary> projects = model.Projects
            .Select(project => SummarizeProject(project, declaredByProject[project.Name]))
            .ToList();

        (Dictionary<(string Source, string Target), int> projectPairs,
            Dictionary<(string Source, string Root), int> externalRoots) = TallyReferences(model);

        List<ProjectEdgeSummary> projectEdges = projectPairs
            .Select(pair => new ProjectEdgeSummary(pair.Key.Source, pair.Key.Target, pair.Value))
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .ToList();

        List<ExternalEdgeSummary> externalEdges = externalRoots
            .Select(reference => new ExternalEdgeSummary(reference.Key.Source, reference.Key.Root, reference.Value))
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetNamespaceRoot, StringComparer.Ordinal)
            .ToList();

        // Types are already ordinal by full name; the sort is spelled anyway so this list's stated order does
        // not depend on the model's.
        List<MultiplyDeclaredTypeSummary> multiplyDeclaredTypes = model.Types
            .Where(type => type.AlsoDeclaredBy.Count > 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new MultiplyDeclaredTypeSummary(type.FullName, DeclarersOf(type), type.ProjectName))
            .ToList();

        return new GraphSummary(projects, projectEdges, externalEdges, multiplyDeclaredTypes, ShadowedTypes(model));
    }

    // A projection of the fact the merge stamped, never re-derived from Types or the edges: the edges see one
    // name per edge minted, which is not every name a project binds.
    private static List<ShadowedTypeSummary> ShadowedTypes(CodebaseModel model)
    {
        return model.ShadowedNames
            .Select(shadowed => new ShadowedTypeSummary(
                shadowed.FullName, shadowed.DeclaredBy, shadowed.SuppliedBy, shadowed.BoundFromAssemblyBy))
            .ToList();
    }

    // The project pairs the code actually declares, one per edge instance that crosses a boundary, and the
    // external namespace roots each project reaches, one per instance whose target is outside the solution.
    // The model carries one node per full name and one edge per type PAIR, so where several projects declare a
    // name, N compilations of one reference collapse into one entry, whose recorded instances unfold it.
    // Reading the pair off the edge's two nodes instead would invent dependencies no project file declares and
    // credit one copy's references to another (GRAMMAR §4.1). An instance is recorded per (maker, reached) pair,
    // so a pair is still counted once per type pair per project pair. One walk, so the two tallies cannot drift
    // on which record they read or on what an unrecorded edge stands for.
    private static (Dictionary<(string Source, string Target), int> ProjectPairs,
        Dictionary<(string Source, string Root), int> ExternalRoots) TallyReferences(CodebaseModel model)
    {
        var projectPairs = new Dictionary<(string Source, string Target), int>();
        var externalNamespaces = new Dictionary<(string Source, string Namespace), int>();

        foreach (ReferenceEdge edge in model.Edges)
        {
            if (edge.Evidence.Instances is not { } recorded)
            {
                CountInstance(edge, EdgeInstances.Implicit(edge), projectPairs, externalNamespaces);
                continue;
            }

            for (var i = 0; i < recorded.Count; i++)
                CountInstance(edge, recorded[i], projectPairs, externalNamespaces);
        }

        // Folded to roots once per distinct namespace a project reaches rather than once per instance.
        var externalRoots = new Dictionary<(string Source, string Root), int>();
        foreach (KeyValuePair<(string Source, string Namespace), int> reached in externalNamespaces)
            Add(externalRoots, (reached.Key.Source, NamespaceRoot(reached.Key.Namespace)), reached.Value);

        return (projectPairs, externalRoots);
    }

    private static void CountInstance(
        ReferenceEdge edge, EdgeInstance instance, Dictionary<(string Source, string Target), int> projectPairs,
        Dictionary<(string Source, string Namespace), int> externalNamespaces)
    {
        if (edge.Target.IsExternal)
            Add(externalNamespaces, (instance.SourceProject, edge.Target.Namespace), 1);
        else if (!instance.IsIntraProject)
            Add(projectPairs, (instance.SourceProject, instance.TargetProject), 1);
    }

    // The per-key counts a survey line carries, tallied rather than grouped: a lookup retains one entry per
    // edge INSTANCE only to read each group's size, where the survey keeps one line per distinct key.
    private static void Add<TKey>(Dictionary<TKey, int> counts, TKey key, int count)
        where TKey : notnull
    {
        counts[key] = counts.TryGetValue(key, out int made) ? made + count : count;
    }

    private static IReadOnlyList<string> DeclarersOf(TypeNode type)
    {
        return type.Declarers
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Narrows a survey to the projects whose name matches one of <paramref name="projectGlobs" /> — a
    ///     complete survey of a smaller subject, not a truncated one. A pattern is matched against the whole
    ///     project name, case-sensitively, with <c>*</c> standing for any run of characters including none, so
    ///     <c>MyApp.*</c> keeps every project whose name begins <c>MyApp.</c>. An empty list narrows nothing and
    ///     hands <paramref name="summary" /> straight back.
    /// </summary>
    /// <remarks>
    ///     What survives is wider than the project list, so a narrowed survey can name projects it does not
    ///     list: a project edge survives when either end matches, which keeps visible who reaches into the
    ///     matched projects, and each surviving <see cref="ProjectSummary" /> keeps its declared references to
    ///     projects outside the narrowing.
    /// </remarks>
    public static GraphSummary Scope(GraphSummary summary, IReadOnlyList<string> projectGlobs)
    {
        Guard.NotNull(summary, nameof(summary));
        Guard.NotNull(projectGlobs, nameof(projectGlobs));

        if (projectGlobs.Count == 0) return summary;

        List<ProjectSummary> projects = summary.Projects
            .Where(project => Wildcard.MatchesAny(projectGlobs, project.Name))
            .ToList();

        List<ProjectEdgeSummary> projectEdges = summary.ProjectEdges
            .Where(edge => Wildcard.MatchesAny(projectGlobs, edge.Source)
                           || Wildcard.MatchesAny(projectGlobs, edge.Target))
            .ToList();

        List<ExternalEdgeSummary> externalEdges = summary.ExternalEdges
            .Where(edge => Wildcard.MatchesAny(projectGlobs, edge.Source))
            .ToList();

        List<MultiplyDeclaredTypeSummary> multiplyDeclaredTypes = summary.MultiplyDeclaredTypes
            .Where(type => type.DeclaredBy.Any(project => Wildcard.MatchesAny(projectGlobs, project)))
            .ToList();

        // Either end keeps the entry, the rule the project edges take: the reader anchored on the product
        // project is the one whose reference reaches the assembly rather than the declaration, and narrowing
        // on the declaring project alone would drop the entry from exactly the scope that needs it.
        List<ShadowedTypeSummary> shadowedTypes = summary.ShadowedTypes
            .Where(type => Wildcard.MatchesAny(projectGlobs, type.DeclaredBy)
                           || type.BoundFromAssemblyBy.Any(project => Wildcard.MatchesAny(projectGlobs, project)))
            .ToList();

        return new GraphSummary(projects, projectEdges, externalEdges, multiplyDeclaredTypes, shadowedTypes);
    }

    private static ProjectSummary SummarizeProject(ProjectNode project, IEnumerable<TypeNode> declared)
    {
        List<TypeNode> declaredTypes = declared.ToList();

        List<NamespaceCount> namespaces = declaredTypes
            .GroupBy(type => type.Namespace)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new NamespaceCount(
                DisplayNamespace(group.Key), group.Count(), group.Count(type => type.IsGenerated)))
            .ToList();

        // The namespace groups partition the declared types, so their generated counts already total the
        // project's.
        int generated = namespaces.Sum(ns => ns.Generated);

        return new ProjectSummary(
            project.Name, project.ProjectReferences, declaredTypes.Count, generated, namespaces,
            project.SolutionMember, project.TargetFrameworks, project.FactsFollow, project.PackageReferences,
            project.IsPackable, project.LocksPackages, project.UndeclaredProjectReferences);
    }

    private static string NamespaceRoot(string @namespace)
    {
        if (@namespace.Length == 0) return GlobalNamespaceLabel;

        // Found by scanning to the second dot rather than splitting, which allocates an array plus a substring
        // per segment to keep two of them.
        int firstDot = @namespace.IndexOf('.');
        if (firstDot < 0) return @namespace;

        int secondDot = @namespace.IndexOf('.', firstDot + 1);
        return secondDot < 0 ? @namespace : @namespace.Substring(0, secondDot);
    }

    private static string DisplayNamespace(string @namespace)
    {
        return @namespace.Length == 0 ? GlobalNamespaceLabel : @namespace;
    }
}
