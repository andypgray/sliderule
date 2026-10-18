using SlideRule.Codebase;
using SlideRule.Internal;

namespace SlideRule.Cli.Rendering;

/// <summary>Formats a <see cref="GraphSummary" /> as the human <c>graph</c> survey.</summary>
/// <remarks>
///     Pure over the summary, so the line shapes are unit-pinned. Mirrors
///     <see cref="StatusFormatter" />'s terse, em-dashed voice; an empty section reads <c>(none)</c> rather
///     than vanishing, so the survey's shape is stable — which is also why overview grain replaces the
///     namespace inventory with an elision line instead of dropping the section: a reader can see what was
///     left out and how to get it back.
/// </remarks>
internal static class GraphFormatter
{
    private const string OverviewElisionLine =
        "  (elided at overview grain — rerun at --grain full for the per-project namespace inventory)";

    private const string SkeletonElisionLine =
        "  (elided at skeleton grain — rerun at --grain full for the external references)";

    private const string SkeletonMultiplyDeclaredElisionLine =
        "  (elided at skeleton grain — rerun at --grain full for the multiply-declared types)";

    private const string SkeletonShadowedElisionLine =
        "  (elided at skeleton grain — rerun at --grain full for the shadowed type names)";

    private const string IndexProjectEdgeElisionLine =
        "  (elided at index grain — rerun at --grain full for the observed project references)";

    /// <summary>
    ///     The survey's lines. A coarser <paramref name="grain" /> renders the same sections with less in
    ///     them, an elided section down to one line naming the flag to drop, and no section ever disappears.
    ///     A coverage statement with nothing to report keeps its <c>(none)</c> at every grain instead, which
    ///     is why a healthy solution's skeleton still says outright that no type is declared twice.
    /// </summary>
    /// <param name="summary">The survey to format.</param>
    /// <param name="solutionName">The solution's file name, for the heading.</param>
    /// <param name="grain">How much of each section to render.</param>
    /// <param name="unsupportedProjects">
    ///     The declared projects the survey does not cover, as the shared trust stamp composed them —
    ///     relativized, and each already carrying its reason, so this line and the document's own key cannot
    ///     disagree about what the run reached. Passed in rather than taken off <paramref name="summary" />
    ///     because it is a fact about the load rather than about the codebase: the summary describes what was
    ///     extracted, and this names what never could be. For that reason it is also unaffected by
    ///     <c>--projects</c>, which scopes the summary alone.
    /// </param>
    public static IReadOnlyList<string> Lines(
        GraphSummary summary, string solutionName, DocumentGrain grain,
        IReadOnlyList<UnsupportedProjectStamp> unsupportedProjects)
    {
        var lines = new List<string> { $"Codebase survey: {solutionName}", "" };

        lines.Add($"Projects ({summary.Projects.Count}):");
        lines.AddRange(summary.Projects.Select(project => ProjectLine(project, grain)));
        lines.Add("");

        lines.Add("Projects the solution declares that this survey does not cover:");
        lines.AddRange(UnsupportedProjectLines(unsupportedProjects));
        lines.Add("");

        lines.Add("Observed project references (distinct type pairs):");
        lines.AddRange(ProjectEdgeLines(summary, grain));
        lines.Add("");

        lines.Add("Types declared by more than one project:");
        lines.AddRange(MultiplyDeclaredTypeLines(summary, grain));
        lines.Add("");

        lines.Add("Type names a referenced assembly also supplies:");
        lines.AddRange(ShadowedTypeLines(summary, grain));
        lines.Add("");

        lines.Add("Namespaces:");
        lines.AddRange(NamespaceLines(summary, grain));
        lines.Add("");

        lines.Add("External references (by namespace root):");
        lines.AddRange(ExternalEdgeLines(summary, grain));

        return lines;
    }

    // Sited straight under the roster, because it is the roster's own caveat: these are the projects that
    // would have been lines above it. Reads "(none)" on a solution the survey covers whole rather than
    // vanishing, per this formatter's standing rule: a section that appears only when it has content is one
    // a reader never learns to look for. Never elided at any grain: it is bounded by the solution rather
    // than the codebase, and a coarser survey is where a reader most needs to know it is of part of the
    // solution.
    private static IEnumerable<string> UnsupportedProjectLines(
        IReadOnlyList<UnsupportedProjectStamp> unsupportedProjects)
    {
        return unsupportedProjects.Count > 0
            ? unsupportedProjects.Select(project => $"  {project.Describe()}")
            : ["  (none)"];
    }

    private static IEnumerable<string> NamespaceLines(GraphSummary summary, DocumentGrain grain)
    {
        return grain >= DocumentGrain.Overview ? [OverviewElisionLine] : summary.Projects.Select(NamespaceLine);
    }

    // Only the passenger is annotated. Membership is the unremarkable case — every project of a healthy
    // solution has it — so marking it would put a badge on every line and leave the one line worth reading
    // no easier to find. An unread membership says nothing at all, for the same reason it serializes absent.
    // What a project targets does not follow that rule: it is not a remark about the project but
    // an answer a reader came for, and the line beside it — what the project references — is stated on every
    // project for exactly that reason.
    private static string ProjectLine(ProjectSummary project, DocumentGrain grain)
    {
        string membership = project.SolutionMember == false ? " (not a solution member)" : "";
        string generated = project.Generated > 0 ? $" ({project.Generated} generated)" : "";
        var line = $"  {project.Name}{membership} — {Plurals.Counted(project.Types, "type")}{generated}";

        // At index grain the line stops here, matching what the document's own row keeps: a project's name,
        // whether the solution declares it, and how big it is. What it declares and what it targets are
        // both qualifiers on that, and both multiply by the project count on the one rung whose whole job is
        // to stay proportional to the roster.
        if (grain >= DocumentGrain.Index) return line;

        string references = project.ProjectReferences.Count > 0 ? string.Join(", ", project.ProjectReferences) : "(none)";
        return $"{line}; {FrameworksClause(project)}references: {references}"
               + UndeclaredClause(project) + PackagesClause(project, grain) + PackagingClause(project);
    }

    // "; undeclared references: C" — the projects compiled against that the declared list does not reach, and
    // only where there are any, on the passenger's rule: the clause marks the one line worth reading, and every
    // healthy line goes without it.
    private static string UndeclaredClause(ProjectSummary project)
    {
        return project.UndeclaredProjectReferences.Count > 0
            ? $"; undeclared references: {string.Join(", ", project.UndeclaredProjectReferences)}"
            : "";
    }

    // "; packages: A, B (private)" — the build-side twin of the external references, and elided at the grain
    // those go at, for their reason: a project's declared packages scale with what the solution depends on
    // rather than with the solution. It reads "(none)" like the references beside it, because a project that
    // declares no packages is the fact a rule about declared packages is written against. A private reference
    // is marked where it is named rather than listed again, on the passenger's rule: the mark lands only on the
    // names MustReferenceNoPackages passes, and leaves with them at skeleton. A package declared for only some of
    // the project's frameworks is marked the same way, "(netstandard2.0 only)", and both marks share one
    // parenthesis where both apply.
    private static string PackagesClause(ProjectSummary project, DocumentGrain grain)
    {
        if (grain >= DocumentGrain.Skeleton) return "";

        IReadOnlyList<PackageReference> packages = project.PackageReferences;
        if (packages.Count == 0) return "; packages: (none)";

        IEnumerable<string> named = packages.Select(package => package.Name + PackageMarks(package, project.TargetFrameworks));

        return $"; packages: {string.Join(", ", named)}";
    }

    private static string PackageMarks(PackageReference package, IReadOnlyList<string> projectFrameworks)
    {
        var marks = new List<string>(2);
        if (package.IsPrivate) marks.Add("private");
        if (package.DeclaredForOnlySomeOf(projectFrameworks)) marks.Add(string.Join(", ", package.Frameworks) + " only");

        return marks.Count == 0 ? "" : $" ({string.Join("; ", marks)})";
    }

    // Only the deliberate half of each fact is annotated, on the passenger's rule: the SDK makes every
    // project packable and NuGet locks no restore, so a badge on those would land on nearly every line and
    // leave the two lines worth reading no easier to find. The document's own row states both outright,
    // because that is the surface a rule is drafted against rather than read on.
    private static string PackagingClause(ProjectSummary project)
    {
        string packable = project.IsPackable == false ? "; does not pack" : "";
        string locks = project.LocksPackages == true ? "; locks restore" : "";
        return packable + locks;
    }

    // "targets net10.0, netstandard2.0 (shared types from net10.0); " — the frameworks in extraction order,
    // and the winner only where the frameworks actually share a type. A multi-targeted project whose
    // frameworks share nothing displaced no facts, so the parenthesis would be a claim about nothing; the
    // list alone still says the project compiles more than once, which is the fact a rule author needs.
    // Past two frameworks the parenthesis states the rule rather than naming one framework: each shared type
    // follows the first of the listed frameworks that declares it, and where they share different sets of
    // types that is not one framework for all of them. With two, only the first can win, so naming it is exact.
    // Empty only where nothing evaluated the project, which is where the survey has nothing to say rather
    // than something unremarkable to leave out.
    private static string FrameworksClause(ProjectSummary project)
    {
        if (project.TargetFrameworks.Count == 0) return "";

        string shared = project.FactsFollow switch
        {
            null => "",
            _ when project.TargetFrameworks.Count > 2 => " (each shared type from the first of these that declares it)",
            { } winner => $" (shared types from {winner})"
        };
        return $"targets {string.Join(", ", project.TargetFrameworks)}{shared}; ";
    }

    private static string NamespaceLine(ProjectSummary project)
    {
        string inventory = project.Namespaces.Count > 0
            ? string.Join(", ", project.Namespaces.Select(NamespaceEntry))
            : "(none)";
        return $"  {project.Name}: {inventory}";
    }

    // A wholly generated namespace reads "all generated" rather than repeating the count it just gave. That
    // is the shape a reader most needs to catch — a compiled view tier collects under one namespace nobody
    // typed, and it is the one namespace here that must never become a layer glob.
    private static string NamespaceEntry(NamespaceCount @namespace)
    {
        if (@namespace.Generated == 0) return $"{@namespace.Namespace} ({@namespace.Types})";

        string qualifier = @namespace.Generated == @namespace.Types ? "all generated" : $"{@namespace.Generated} generated";
        return $"{@namespace.Namespace} ({@namespace.Types}, {qualifier})";
    }

    // The last section to elide, and the one that decides where the ladder stops: observed edges are the
    // survey's remaining codebase-scaled array once the namespaces and external rows are gone. Unlike the two
    // coverage statements below it, an empty edge list is not a finding worth keeping at every grain — a
    // solution whose projects never reference each other still reads truthfully from the roster — so this
    // section takes the ordinary rule and elides whether or not it has rows.
    private static IEnumerable<string> ProjectEdgeLines(GraphSummary summary, DocumentGrain grain)
    {
        if (grain >= DocumentGrain.Index) return [IndexProjectEdgeElisionLine];

        return summary.ProjectEdges.Count > 0
            ? summary.ProjectEdges.Select(e => $"  {e.Source} -> {e.Target}: {e.References}")
            : ["  (none)"];
    }

    // The survey's coverage statement, sited straight after the edges it explains: one reason a project
    // pair is absent from the block above is that one of them compiles the other's type itself. It reads
    // "(none)" on a healthy solution rather than vanishing, for the same reason every other section does —
    // a section that only appears when it has content is one a reader never learns to look for.
    private static IEnumerable<string> MultiplyDeclaredTypeLines(GraphSummary summary, DocumentGrain grain)
    {
        return CoverageSection(
            summary.MultiplyDeclaredTypes, grain, SkeletonMultiplyDeclaredElisionLine, MultiplyDeclaredTypeLine);
    }

    private static string MultiplyDeclaredTypeLine(MultiplyDeclaredTypeSummary type)
    {
        return $"  {type.Type} — declared by {string.Join(", ", type.DeclaredBy)}; "
               + $"facts follow {type.FactsFollow}";
    }

    // The second coverage statement, sited beside the first because they answer the same question — why a
    // selection over a project does not reach every use of a name it declares. This one's answer is that the
    // name means two types, and the line names both halves plus who reaches the far one.
    private static IEnumerable<string> ShadowedTypeLines(GraphSummary summary, DocumentGrain grain)
    {
        return CoverageSection(summary.ShadowedTypes, grain, SkeletonShadowedElisionLine, ShadowedTypeLine);
    }

    // No empty-roster arm: an entry exists only because some project's compilation bound the name from the
    // assembly, and the merge records that project where it knows it rather than inferring it from the edges,
    // so "which nothing binds" is a sentence this line is never asked to write.
    private static string ShadowedTypeLine(ShadowedTypeSummary type)
    {
        return $"  {type.Type} — declared by {type.DeclaredBy}; also supplied by {string.Join(", ", type.SuppliedBy)}, "
               + $"which {string.Join(", ", type.BoundFromAssemblyBy)} binds";
    }

    // The rule both coverage statements above take, and the one that sets them apart from the sections
    // around them: an empty statement still reads "(none)" at every grain, because having nothing to report
    // IS its finding, and only a non-empty one elides its rows at skeleton, where the list scales with the
    // codebase and the fact that there is something there to read is what a coarse survey has room for.
    private static IEnumerable<string> CoverageSection<T>(
        IReadOnlyList<T> items, DocumentGrain grain, string elisionLine, Func<T, string> line)
    {
        if (items.Count == 0) return ["  (none)"];

        return grain >= DocumentGrain.Skeleton ? [elisionLine] : items.Select(line);
    }

    private static IEnumerable<string> ExternalEdgeLines(GraphSummary summary, DocumentGrain grain)
    {
        if (grain >= DocumentGrain.Skeleton) return [SkeletonElisionLine];

        return summary.ExternalEdges.Count > 0
            ? summary.ExternalEdges.Select(e => $"  {e.Source} -> {e.TargetNamespaceRoot}: {e.References}")
            : ["  (none)"];
    }
}
