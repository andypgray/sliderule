using System.Text.Json;
using SlideRule.Codebase;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Renders a <see cref="GraphSummary" /> as the <c>graph --json</c> document (its own
///     <c>schemaVersion</c>, <see cref="SchemaVersion" />) — the pre-spec codebase survey.
/// </summary>
/// <remarks>
///     The document is composed as a string rather than written straight out, so a caller with a response
///     budget can measure the full survey and, if it overruns, re-compose it a rung coarser from the same
///     summary — one extraction, one render per rung walked, and never a document cut mid-array.
/// </remarks>
internal static class GraphJsonRenderer
{
    /// <summary>The version of the document this writes.</summary>
    internal const int SchemaVersion = 1;

    /// <summary>
    ///     The survey document as a string. <paramref name="grain" /> decides how much of each project is
    ///     rendered and stamps itself on the document; <paramref name="projectsScope" /> is the filter the
    ///     summary was already narrowed by, recorded so the document says what it covers.
    ///     <paramref name="workspaceDiagnostics" /> is the rendered stream the caller composed and
    ///     <paramref name="trust" /> the stamp it read off the load — the project lists the document carries,
    ///     and the grouped advisories that become <c>packageAudit</c>. Both are composed once by the caller
    ///     and handed to every rung, because a ladder renders the same load several times over.
    /// </summary>
    public static string Document(
        GraphSummary summary,
        string solutionName,
        IReadOnlyList<string> workspaceDiagnostics,
        WorkspaceTrustStamp trust,
        DocumentGrain grain,
        IReadOnlyList<string> projectsScope)
    {
        bool skeleton = grain >= DocumentGrain.Skeleton;
        bool index = grain >= DocumentGrain.Index;

        // The coverage statements ride the same rung as the external rows, and for the same reason: their
        // length scales with the codebase, so a skeleton carries the count instead. The load's own two rides
        // one rung lower, at index, where MSBuild's words and the audit's go together.
        (IReadOnlyList<GraphMultiplyDeclaredTypeJson>? multiplyDeclaredRows, int? multiplyDeclaredCount) =
            DocumentGrains.CoverageRung(
                summary.MultiplyDeclaredTypes, skeleton,
                t => new GraphMultiplyDeclaredTypeJson(t.Type, t.DeclaredBy, t.FactsFollow));
        (IReadOnlyList<GraphShadowedTypeJson>? shadowedRows, int? shadowedCount) =
            DocumentGrains.CoverageRung(
                summary.ShadowedTypes, skeleton,
                t => new GraphShadowedTypeJson(t.Type, t.DeclaredBy, t.SuppliedBy, t.BoundFromAssemblyBy));
        (IReadOnlyList<PackageAuditEntry>? packageAuditRows, int? packageAuditCount) =
            DocumentGrains.CoverageRung(trust.PackageAudit ?? [], index);
        (IReadOnlyList<string>? diagnosticRows, int? diagnosticCount) =
            DocumentGrains.CoverageRung(workspaceDiagnostics, index);

        var document = new GraphJson(
            SchemaVersion,
            solutionName,
            grain.Wire(),
            SlideRuleJson.OmitEmpty(projectsScope),
            summary.Projects.Select(project => ToProject(project, grain)).ToList(),
            index
                ? null
                : summary.ProjectEdges.Select(e => new GraphProjectEdgeJson(e.Source, e.Target, e.References)).ToList(),
            index ? summary.ProjectEdges.Count : null,
            skeleton
                ? null
                : summary.ExternalEdges.Select(e => new GraphExternalEdgeJson(e.Source, e.TargetNamespaceRoot, e.References)).ToList(),
            skeleton ? summary.ExternalEdges.Count : null,
            multiplyDeclaredRows,
            multiplyDeclaredCount,
            shadowedRows,
            shadowedCount,
            packageAuditRows,
            packageAuditCount,
            diagnosticRows,
            diagnosticCount,
            trust.ModelIncomplete,
            trust.FailedProjects,
            trust.UncheckedProjects,
            trust.RestoreFailedProjects,
            trust.UnsupportedProjects);

        return JsonSerializer.Serialize(document, SlideRuleJson.Context.GraphJson);
    }

    // The row's own ladder. What a project targets, whether it packs and whether it locks scale with the
    // solution's projects, so they survive every rung down to index, where the row is cut to what names
    // and sizes a project. Each is omitted where it has no answer, which
    // for the frameworks means no evaluation happened at all. The declared project references are the reason
    // index exists: they scale with the solution's edges rather than its projects, so on a large solution
    // they are most of the roster's bulk. The declared packages leave a rung earlier, at skeleton, beside
    // the external-reference rows they are the build-side twin of, and the private subset leaves with them. The
    // undeclared references qualify the declared ones, so they leave at index with them and are absent when empty.
    // Both elide bare, as the namespaces do at overview — a row's array needs no count of its own when the
    // document already stamps the grain that dropped it. The private subset is also absent where it is
    // empty, because a non-null empty list serializes as [] on every row of every solution without one. The
    // package frameworks are absent where there are none, for the private subset's reason: a solution with no
    // framework-conditioned package surveys exactly as it did before the key existed.
    private static GraphProjectJson ToProject(ProjectSummary project, DocumentGrain grain)
    {
        bool index = grain >= DocumentGrain.Index;
        bool skeleton = grain >= DocumentGrain.Skeleton;

        return new GraphProjectJson(
            project.Name,
            project.SolutionMember,
            index ? null : SlideRuleJson.OmitEmpty(project.TargetFrameworks),
            index ? null : project.FactsFollow,
            index ? null : project.IsPackable,
            index ? null : project.LocksPackages,
            index ? null : project.ProjectReferences,
            index ? null : SlideRuleJson.OmitEmpty(project.UndeclaredProjectReferences),
            skeleton ? null : PackageNames(project.PackageReferences, privateOnly: false),
            skeleton ? null : SlideRuleJson.OmitEmpty(PackageNames(project.PackageReferences, privateOnly: true)),
            skeleton ? null : SlideRuleJson.OmitEmpty(PackageFrameworks(project)),
            project.Types,
            SlideRuleJson.OmitZero(project.Generated),
            grain >= DocumentGrain.Overview
                ? null
                : project.Namespaces.Select(ToNamespace).ToList());
    }

    // The document spells a project's packages as two name lists, every declared package and then the private ones
    // again, a subset in the same order: a reader of the private key needs no join to know which names it qualifies.
    private static IReadOnlyList<string> PackageNames(IReadOnlyList<PackageReference> packages, bool privateOnly)
    {
        return packages
            .Where(package => !privateOnly || package.IsPrivate)
            .Select(package => package.Name)
            .ToList();
    }

    // The packages the project declares for only some of its frameworks, each with the frameworks that declare it,
    // in package order.
    private static IReadOnlyList<GraphPackageFrameworksJson> PackageFrameworks(ProjectSummary project)
    {
        return project.PackageReferences
            .Where(package => package.DeclaredForOnlySomeOf(project.TargetFrameworks))
            .Select(package => new GraphPackageFrameworksJson(package.Name, package.Frameworks))
            .ToList();
    }

    // Omitted at zero on both keys, which is what keeps the survey of a solution with no generators exactly
    // the document it was before the key existed — and keeps the skeleton grain inside its token budget.
    private static GraphNamespaceJson ToNamespace(NamespaceCount @namespace)
    {
        return new GraphNamespaceJson(
            @namespace.Namespace, @namespace.Types, SlideRuleJson.OmitZero(@namespace.Generated));
    }
}
