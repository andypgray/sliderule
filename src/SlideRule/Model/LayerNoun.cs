using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     A named layer defined by one or more namespace globs — <c>arch.Layer("Domain", "MyApp.Domain.*")</c>
///     — or by a <see cref="Selection" />: <c>arch.Layer("Core", arch.Project("MyApp.Core"))</c>.
///     Reference fragment: "the Domain layer" (GRAMMAR §5.1). A bare <see cref="Layer" /> subject with zero
///     adjectives speaks in the collective voice (GRAMMAR §6).
/// </summary>
/// <remarks>
///     A layer is transparent to its <see cref="Definition" />: it names exactly what the definition names,
///     at the projects the definition names them at, and its own adjectives apply after. The glob form is
///     not desugared to a union of namespace nouns — its one-scan evaluation stands, and a glob matching
///     nothing stays a placement skip rather than an empty operand.
/// </remarks>
internal sealed class LayerNoun : SelectionNoun
{
    internal LayerNoun(string name, IReadOnlyList<string> globs)
    {
        Name = name;
        Globs = globs;
    }

    internal LayerNoun(string name, Selection definition)
    {
        Name = name;
        Globs = RegionOf(definition);
        Definition = definition;
    }

    /// <summary>The layer name, which the sentence fragments render as prose and never backtick.</summary>
    internal string Name { get; }

    /// <summary>
    ///     The namespace globs the glob form declares (at least one), or — for a definition — the namespace
    ///     region the definition names, or empty when it names none.
    /// </summary>
    internal IReadOnlyList<string> Globs { get; }

    /// <summary>The selection that defines the layer, or null for the glob form.</summary>
    internal Selection? Definition { get; }

    internal override string Locative => $" in the {Name} layer";

    internal override string ReferenceFragment => $"the {Name} layer";

    internal override string CollapsedLocative(IReadOnlyList<SelectionNoun> group)
    {
        return $" in the {LayerNames(group)} layers";
    }

    internal override string CollapsedReference(IReadOnlyList<SelectionNoun> group)
    {
        return $"the {LayerNames(group)} layers";
    }

    /// <summary>
    ///     The namespace region a definition names, or empty when it names none — what
    ///     <see cref="Globs" /> carries for the definition form.
    /// </summary>
    /// <remarks>
    ///     Two shapes, the ones the law drawing calls a namespace place: a namespace noun, and <c>arch.Types</c>
    ///     narrowed by exactly one <c>InNamespace</c>. Adjectives beside them narrow which types inside the region
    ///     the layer holds, not where the region is, so they do not move it. The drawing's third shape, a
    ///     place-shaped noun narrowed by one <c>InNamespace</c>, is deliberately not shared: a region answered for
    ///     it would fill <see cref="Globs" /> for a refinement-defined layer, routing it down the classifier's glob
    ///     arm and costing it its declared parent and the box it was nested in.
    /// </remarks>
    internal static IReadOnlyList<string> RegionOf(Selection definition)
    {
        if (definition is UnionSelection) return Array.Empty<string>();

        if (definition.Noun is NamespaceNoun ns) return [ns.Glob];
        if (definition.Noun is not TypesNoun) return Array.Empty<string>();

        List<InNamespaceAdjective> globs = definition.Adjectives.OfType<InNamespaceAdjective>().ToList();
        return globs.Count == 1 ? [globs[0].Glob] : Array.Empty<string>();
    }

    /// <summary>
    ///     The project a definition names when it is exactly that project — a project noun with no
    ///     adjective — or null for no definition, a union, a narrowed selection or any other noun.
    /// </summary>
    internal static string? ProjectOf(Selection? definition)
    {
        if (definition is null or UnionSelection || definition.Adjectives.Count > 0) return null;

        return definition.Noun is ProjectNoun project ? project.Name : null;
    }

    private static string LayerNames(IReadOnlyList<SelectionNoun> group)
    {
        return ProseFormat.JoinReferences(group.Select(noun => ((LayerNoun)noun).Name).ToList());
    }
}
