using System.Text;
using SlideRule.Codebase;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     Renders every fact of a <see cref="CodebaseModel" /> to a single deterministic string, for
///     string-equality pins.
/// </summary>
/// <remarks>
///     The dump is total by contract: a fact this file does not render is pinned by no dump comparison,
///     so a new model fact must gain a line here or it ships unguarded. The model arrives fully ordered,
///     so a straight walk is stable without sorting.
/// </remarks>
internal static class ModelDump
{
    public static string Render(CodebaseModel model)
    {
        var builder = new StringBuilder();

        builder.AppendLine("== PROJECTS ==");
        foreach (ProjectNode project in model.Projects)
            RenderProject(builder, project);

        builder.AppendLine("== TYPES ==");
        foreach (TypeNode type in model.Types)
            RenderType(builder, type);

        builder.AppendLine("== EDGES ==");
        foreach (ReferenceEdge edge in model.Edges)
            builder.Append(Endpoint(edge.Source))
                .Append(" -> ")
                .Append(Endpoint(edge.Target))
                .AppendLine(RenderEvidence(edge.Evidence));

        builder.AppendLine("== MEMBER EDGES ==");
        foreach (MemberEdge edge in model.MemberEdges)
            builder.Append(Endpoint(edge.Source))
                .Append(" -> ")
                .Append(edge.Member.SymbolId)
                .Append(" (")
                .Append(edge.Member.Kind)
                .Append(' ')
                .Append(Endpoint(edge.Member.ContainingType))
                .Append('.')
                .Append(edge.Member.Name)
                .Append(')')
                .AppendLine(RenderEvidence(edge.Evidence));

        builder.AppendLine("== CONSTRUCTOR EDGES ==");
        foreach (ConstructorEdge edge in model.ConstructorEdges)
            builder.Append(Endpoint(edge.Source))
                .Append(" -> ")
                .Append(Endpoint(edge.Constructed))
                .AppendLine(RenderEvidence(edge.Evidence));

        builder.AppendLine("== INJECTION EDGES ==");
        foreach (InjectionEdge edge in model.InjectionEdges)
            builder.Append(Endpoint(edge.Source))
                .Append(" -> ")
                .Append(Endpoint(edge.Injected))
                .AppendLine(RenderEvidence(edge.Evidence));

        // The catch line renders all three site lists: the totality contract means the unfiltered subset and the
        // swallowing subset within it (§4.8) must show up here or neither is pinned by any dump comparison. An
        // all-filtered edge renders `unfiltered=[] swallowing=[]`; an all-rethrowing one `swallowing=[]`.
        builder.AppendLine("== CATCH EDGES ==");
        foreach (CatchEdge edge in model.CatchEdges)
            builder.Append(Endpoint(edge.Source))
                .Append(" -> ")
                .Append(Endpoint(edge.Caught))
                .Append(" @ [")
                .Append(RenderSites(edge.Sites))
                .Append("] unfiltered=[")
                .Append(RenderSites(edge.UnfilteredSites))
                .Append("] swallowing=[")
                .Append(RenderSites(edge.SwallowingSites))
                .Append(']')
                .Append(RenderInstances(" instances=", edge.Evidence.Instances))
                .Append(RenderInstances(" unfilteredInstances=", edge.UnfilteredEvidence.Instances))
                .AppendLine(RenderInstances(" swallowingInstances=", edge.SwallowingEvidence.Instances));

        builder.AppendLine("== THROW EDGES ==");
        foreach (ThrowEdge edge in model.ThrowEdges)
            builder.Append(Endpoint(edge.Source))
                .Append(" -> ")
                .Append(Endpoint(edge.Thrown))
                .AppendLine(RenderEvidence(edge.Evidence));

        builder.AppendLine("== EXPOSURE EDGES ==");
        foreach (ExposureEdge edge in model.ExposureEdges)
            builder.Append(Endpoint(edge.Source))
                .Append(" -> ")
                .Append(Endpoint(edge.Exposed))
                .AppendLine(RenderEvidence(edge.Evidence));

        builder.AppendLine("== REGISTRATIONS ==");
        foreach (ServiceRegistration registration in model.ServiceRegistrations)
            builder.Append(registration.Lifetime)
                .Append(' ')
                .Append(registration.ServiceFullName)
                .Append(" -> ")
                .Append(registration.ImplementationFullName ?? "<none>")
                .Append(" @ [")
                .Append(RenderSites(registration.Sites))
                .AppendLine("]");

        // The one model fact that is about a NAME rather than about a node, so nothing above renders it: two
        // nodes wearing one name are each dumped, but which name the merge concluded was shadowed, and by
        // whom, is only stated here. Empty for every bed but the shadowed-name ones.
        builder.AppendLine("== SHADOWED NAMES ==");
        foreach (ShadowedName shadowed in model.ShadowedNames)
            builder.Append(shadowed.FullName)
                .Append(" declared=")
                .Append(shadowed.DeclaredBy)
                .Append(" supplied=[")
                .Append(string.Join(", ", shadowed.SuppliedBy))
                .Append("] bound=[")
                .Append(string.Join(", ", shadowed.BoundFromAssemblyBy))
                .AppendLine("]");

        builder.AppendLine("== DECLARED MEMBERS ==");
        foreach (TypeNode type in model.Types)
        foreach (MemberNode member in type.Members)
            RenderMember(builder, type, member);

        return builder.ToString();
    }

    // Every fact a project node carries: a dump comparison can only see a dropped field if the dump prints
    // it. A site renders as its own file:line or <null>, so a fact that loses its declaration is a visible
    // diff rather than a silent one.
    private static void RenderProject(StringBuilder builder, ProjectNode project)
    {
        builder.Append(project.Name)
            .Append(" -> [")
            .Append(string.Join(", ", project.ProjectReferences))
            .AppendLine("]");
        builder.Append("  assemblies=[")
            .Append(string.Join(", ", project.AssemblyNames))
            .AppendLine("]");
        builder.Append("  solutionMember=")
            .Append(Tristate(project.SolutionMember))
            .Append(" targets=[")
            .Append(string.Join(", ", project.TargetFrameworks))
            .Append("] @ ")
            .Append(Site(project.TargetFrameworksSite))
            .Append(" factsFollow=")
            .Append(project.FactsFollow ?? "<null>")
            .AppendLine();
        builder.Append("  isPackable=")
            .Append(Tristate(project.IsPackable))
            .Append(" @ ")
            .Append(Site(project.IsPackableSite))
            .Append(" locksPackages=")
            .Append(Tristate(project.LocksPackages))
            .Append(" @ ")
            .Append(Site(project.LocksPackagesSite))
            .AppendLine();
        builder.Append("  packages=[")
            .Append(string.Join(", ", project.PackageReferences.Select(Package)))
            .AppendLine("]");
    }

    private static string Package(PackageReference package)
    {
        string privacy = package.IsPrivate ? " (private)" : "";
        return $"{package.Name} @ {package.Site}{privacy}";
    }

    private static string Tristate(bool? value)
    {
        return value?.ToString() ?? "<null>";
    }

    private static string Site(SourceLocation? location)
    {
        return location?.ToString() ?? "<null>";
    }

    private static void RenderMember(StringBuilder builder, TypeNode type, MemberNode member)
    {
        builder.Append("MEMBER ")
            .Append(type.FullName)
            .Append(' ')
            .AppendLine(member.SymbolId);
        builder.Append("  name=")
            .Append(member.Name)
            .Append(" kind=")
            .Append(member.Kind)
            .Append(" acc=")
            .Append(member.Accessibility)
            .Append(" static=")
            .Append(member.IsStatic)
            .Append(" abstract=")
            .Append(member.IsAbstract)
            .Append(" virtual=")
            .Append(member.IsVirtual)
            .Append(" override=")
            .Append(member.IsOverride)
            .Append(" sealed=")
            .Append(member.IsSealed)
            .Append(" async=")
            .Append(member.IsAsync)
            .AppendLine();
        builder.Append("  hasSetter=")
            .Append(member.HasSetter)
            .Append(" hasInitOnlySetter=")
            .Append(member.HasInitOnlySetter)
            .Append(" readOnly=")
            .Append(member.IsReadOnly)
            .Append(" const=")
            .Append(member.IsConst)
            .AppendLine();
        builder.Append("  returnType=")
            .Append(member.ReturnTypeFullName ?? "<null>")
            .Append(" memberType=")
            .Append(member.MemberTypeFullName ?? "<null>")
            .AppendLine();
        builder.Append("  parameters=[")
            .Append(RenderParameters(member.Parameters))
            .AppendLine("]");
        builder.Append("  attributes=[")
            .Append(RenderAttributes(member.Attributes))
            .AppendLine("]");
        builder.Append("  declSites=[")
            .Append(RenderSites(member.DeclarationSites))
            .AppendLine("]");
        builder.Append("  filePaths=[")
            .Append(string.Join(", ", member.FilePaths))
            .AppendLine("]");
    }

    // Each declared parameter as name:type (the same colon convention a SourceLocation's file:line uses), in
    // declaration order; an empty parameter list renders as [] like the other empty collections in the dump.
    private static string RenderParameters(IReadOnlyList<IParameterInfo> parameters)
    {
        return string.Join(", ", parameters.Select(p => $"{p.Name}:{p.TypeFullName}"));
    }

    // Each declared attribute as definition::constructed — the convention RenderConstruction already uses for
    // the type-side construction lists, so a generic attribute's substituted arguments are pinned too; an empty
    // attribute list renders as [] like the other empty collections in the dump. Rendering it here is what lets
    // the round-trip comparison see a dropped member-attribute field at all.
    private static string RenderAttributes(IReadOnlyList<IAttributeInfo> attributes)
    {
        return string.Join(", ", attributes.Select(a => $"{a.DefinitionFullName}::{a.FullName}"));
    }

    private static void RenderType(StringBuilder builder, TypeNode type)
    {
        builder.Append("TYPE ")
            .AppendLine(type.FullName);
        builder.Append("  symbolId=")
            .AppendLine(type.SymbolId);
        builder.Append("  name=")
            .Append(type.Name)
            .Append(" ns=")
            .AppendLine(type.Namespace);
        builder.Append("  kind=")
            .Append(type.Kind)
            .Append(" acc=")
            .Append(type.Accessibility)
            .Append(" sealed=")
            .Append(type.IsSealed)
            .Append(" static=")
            .Append(type.IsStatic)
            .Append(" abstract=")
            .Append(type.IsAbstract)
            .Append(" record=")
            .Append(type.IsRecord)
            .Append(" generated=")
            .Append(type.IsGenerated)
            .AppendLine();
        builder.Append("  project=")
            .Append(type.ProjectName)
            .Append(" external=")
            .Append(type.IsExternal)
            .AppendLine();
        builder.Append("  declSites=[")
            .Append(RenderSites(type.DeclarationSites))
            .AppendLine("]");
        builder.Append("  filePaths=[")
            .Append(string.Join(", ", type.FilePaths))
            .AppendLine("]");
        builder.Append("  baseType=")
            .AppendLine(FullNameOf(type.BaseType));
        builder.Append("  interfaces=[")
            .Append(string.Join(", ", type.Interfaces.Select(FullNameOf)))
            .AppendLine("]");
        builder.Append("  attributes=[")
            .Append(string.Join(", ", type.Attributes.Select(FullNameOf)))
            .AppendLine("]");
        builder.Append("  allInterfaces=[")
            .Append(string.Join(", ", type.AllInterfaces.Select(RenderConstruction)))
            .AppendLine("]");
        builder.Append("  baseChain=[")
            .Append(string.Join(", ", type.BaseTypeChain.Select(RenderConstruction)))
            .AppendLine("]");
        builder.Append("  attrConstructions=[")
            .Append(string.Join(", ", type.AttributeConstructions.Select(RenderConstruction)))
            .AppendLine("]");
    }

    private static string RenderSites(IReadOnlyList<SourceLocation> sites)
    {
        return string.Join(", ", sites.Select(s => s.ToString()));
    }

    // An edge's recorded instances, each as the project that made it, the project whose copy it reached and its
    // own share of the sites. An edge with no record renders nothing at all, so the dump of a codebase that declares
    // no name twice carries no instance text.
    private static string RenderInstances(string label, IReadOnlyList<EdgeInstance>? instances)
    {
        if (instances is null) return "";

        IEnumerable<string> rendered = instances.Select(instance => $"{instance.SourceProject}->{instance.TargetProject} @ [{RenderSites(instance.Sites)}]");
        return $"{label}[{string.Join(", ", rendered)}]";
    }

    // An edge's evidence as the one value the model holds it as: its sites, then the instances that made them.
    private static string RenderEvidence(EdgeEvidence evidence)
    {
        string sites = RenderSites(evidence.Sites);
        string instances = RenderInstances(" instances=", evidence.Instances);
        return $" @ [{sites}]{instances}";
    }

    // Renders both halves of a construction: the definition node's FullName (the reference-equality target)
    // and the constructed display name — so a closed generic's substituted arguments are pinned too.
    private static string RenderConstruction(TypeConstruction construction)
    {
        return $"{FullNameOf(construction.Definition)}::{construction.FullName}";
    }

    private static string FullNameOf(ITypeInfo? info)
    {
        return info is TypeNode node ? Endpoint(node) : "<null>";
    }

    // An endpoint's node, not merely its name. A full name a project declares and a referenced assembly
    // also supplies denotes two nodes, and on the name alone an edge into one renders exactly like an edge
    // into the other — leaving this dump untotal for precisely the comparison that needs it most, a cache
    // hit or a replay that resolved an endpoint to the wrong one of the two.
    private static string Endpoint(TypeNode node)
    {
        return $"{node.FullName}@{node.ProjectName}";
    }
}
