using SlideRule.Fluent;
using SlideRule.Hosting;
using SlideRule.Model;

namespace SlideRule.Rendering;

/// <summary>
///     The triage behind the law diagram: which verbs draw as an edge, and which selections land on a
///     place a node can stand for.
/// </summary>
/// <remarks>
///     The approximation is noun-level, with one exception. A noun names a region of the codebase, and a
///     subtractive adjective (<c>Except</c>, <c>Named</c>, <c>Where</c>, <c>Authored</c>) narrows which
///     types inside it the rule governs without moving the node, because a diagram has no room to say
///     "except these four types" and <c>sliderule explain</c> carries the exact subject. The exception is
///     the locative <c>InNamespace</c> (<see cref="FromLocative" />). A union has no single noun, a
///     registration is a lifetime rather than a location, bare <c>arch.Types</c> is the whole solution, and
///     a family (<c>arch.Each</c>) is several places at once, so none of them is a place.
/// </remarks>
internal static class LawPlaceClassifier
{
    /// <summary>
    ///     The word an exposure edge's label carries, shared because the legend row that explains that label
    ///     is gated on the verb rather than on the composed arrow text.
    /// </summary>
    internal const string ExposeVerb = "expose";

    private const string OnlyVerb = "only";

    /// <summary>
    ///     What the fence draws for a verb, or null for a verb that constrains a shape, a name, or a member
    ///     rather than a relation between two places, where an arrow would misrepresent it. One arrow cannot
    ///     say the cross-cell ban or the cycle gate either.
    /// </summary>
    /// <remarks>
    ///     One reading answers the whole drawing, so a verb can never be one thing to the arrow and another to
    ///     the legend. The leaf form is drawable and draws nothing: it names no operand, so it registers its
    ///     subject's place and then falls to the compact list under the fence. Declining it here instead would
    ///     drop the place with it, and a leaf is a node of the graph whether or not an arrow leaves it.
    /// </remarks>
    internal static DrawableVerb? Classify(Constraint? constraint)
    {
        return constraint switch
        {
            MustNotReferenceConstraint
                or MustNotBeReferencedByConstraint
                or MustOnlyReferenceConstraint
                or MustOnlyReferenceItselfConstraint
                or MustOnlyBeReferencedByConstraint
                or MustOnlyBeReferencedByItselfConstraint => DrawableReference(constraint),
            MustNotExposeConstraint => new DrawableVerb(inbound: false, only: false, verbWord: ExposeVerb),
            _ => null
        };
    }

    // A reference verb's direction and allow-list are read from the model, where the one-project
    // classification reads them too, so the drawing and the checker cannot disagree about either.
    private static DrawableVerb DrawableReference(Constraint constraint)
    {
        bool inbound = ReferenceVerbs.SubjectAtTarget(constraint);
        bool only = ReferenceVerbs.AllowsSubject(constraint);
        string? verbWord = only ? OnlyVerb : null;
        return new DrawableVerb(inbound, only, verbWord);
    }

    internal static bool IsDrawableVerb(Constraint? constraint)
    {
        return Classify(constraint) is not null;
    }

    /// <summary>
    ///     The place a rule subject stands on, or null when the subject is not place-shaped. A single
    ///     type is not a subject place: a rule anchored on one type is a statement about that type's
    ///     obligations, and drawing the whole architecture around it inverts the scale of the picture.
    /// </summary>
    internal static LawPlace? SubjectPlace(Selection? selection, IReadOnlyList<LayerDefinition> layers)
    {
        return PlaceOf(selection, layers, PlacePosition.Subject);
    }

    /// <summary>
    ///     The place an operand stands on, or null when it is not place-shaped. A single type is a place
    ///     here: <c>MustNotReference(typeof(Environment))</c> names exactly one thing to point the arrow at,
    ///     and that is the whole of what the rule forbids.
    /// </summary>
    internal static LawPlace? OperandPlace(Selection? selection, IReadOnlyList<LayerDefinition> layers)
    {
        return PlaceOf(selection, layers, PlacePosition.Operand);
    }

    private static LawPlace FromGlob(string glob, IReadOnlyList<LayerDefinition> layers)
    {
        // Identity collapse: a layer defined by exactly one glob IS that glob, so a rule naming the glob
        // and a rule naming the layer draw one node under the layer's name. A multi-glob layer is a set
        // no single glob equals, so it never collapses.
        LayerDefinition? owner = layers.FirstOrDefault(layer => layer.Globs.Count == 1 && string.Equals(layer.Globs[0], glob, StringComparison.Ordinal));

        return owner is null
            ? new LawPlace(glob, glob, IdSourceOf(glob), [glob], false)
            : new LawPlace(glob, owner.Name, owner.Name, owner.Globs, true);
    }

    /// <summary>The place one sanctioned-surface operand stands on, or null when it is not place-shaped.</summary>
    internal static LawPlace? FacadePlace(Selection? selection, IReadOnlyList<LayerDefinition> layers)
    {
        return PlaceOf(selection, layers, PlacePosition.Facade);
    }

    private static LawPlace FromFacade(TypeAnchor anchor)
    {
        // The facade's box sits inside the scope's box, which already names the scope, so the simple name
        // is unambiguous where a free-floating type node's would not be.
        return new LawPlace(TypeKey(anchor), anchor.SimpleDisplay, anchor.SimpleDisplay, [], false) { IsFacade = true };
    }

    // Which position of a rule the selection was read in — the one fact that changes what a single type
    // stands for: nothing as a subject, a free-standing node as an operand, and a box inside the scope's
    // box as a sanctioned surface. A region is the same place whichever position names it.
    private enum PlacePosition
    {
        Subject,
        Operand,
        Facade
    }

    private static LawPlace? PlaceOf(Selection? selection, IReadOnlyList<LayerDefinition> layers, PlacePosition position)
    {
        // A union carries no noun head at all, and reading one throws; the guard comes before every
        // other question about the selection.
        if (selection is null or UnionSelection) return null;

        switch (selection.Noun)
        {
            case TypeNoun type when position == PlacePosition.Facade:
                return FromFacade(type.Anchor);
            case TypeNoun type when position == PlacePosition.Operand:
                return FromType(type.Anchor);
            case TypesNoun:
                return FromTypes(selection, layers);
            default:
                // A region noun — a layer, a namespace, a project — is the same place in every position,
                // and a layer definition's own head is read through the same arm.
                return FromLocative(selection, layers);
        }
    }

    private static LawPlace? NounPlace(SelectionNoun noun, IReadOnlyList<LayerDefinition> layers)
    {
        return noun switch
        {
            LayerNoun layer => FromLayer(layer, layers),
            NamespaceNoun @namespace => FromGlob(@namespace.Glob, layers),
            ProjectNoun project => FromProject(project.Name, layers),
            _ => null
        };
    }

    // The one adjective that moves a node. A place-shaped noun narrowed by exactly one InNamespace says where
    // a region sits, not which types it holds, so it draws as that region inside the head's own place: the
    // declared containment a refinement-defined layer earns in FromLayer, without minting a layer to declare
    // it. The region takes FromGlob's key, so a rule naming the glob and a rule naming it locatively stay one
    // node.
    private static LawPlace? FromLocative(Selection selection, IReadOnlyList<LayerDefinition> layers)
    {
        LawPlace? head = NounPlace(selection.Noun, layers);
        if (head is null || selection.Adjectives.Count != 1) return head;

        if (selection.Adjectives[0] is not InNamespaceAdjective locative) return head;

        LawPlace region = FromGlob(locative.Glob, layers);

        // The identity collapse can make the region and its head the same place — a layer narrowed to the
        // one glob that defines it. Registering a place inside itself adds its key twice and throws, so the
        // head stands alone; the adjective named the region the head already is.
        return Encloses(head, region.Key) ? head : Parented(region, head);
    }

    private static LawPlace Parented(LawPlace region, LawPlace parent)
    {
        region.Parent = parent;
        return region;
    }

    // Whether a key is already on a place's parent chain. Walking the whole chain rather than comparing the
    // one candidate keeps the containment a strict order, which is what lets the drawing recurse at all.
    private static bool Encloses(LawPlace? place, string key)
    {
        for (LawPlace? candidate = place; candidate is not null; candidate = candidate.Parent)
            if (string.Equals(candidate.Key, key, StringComparison.Ordinal))
                return true;

        return false;
    }

    // A layer's name is its identity in the spec's own vocabulary, so it is the label whatever defines it.
    // The key follows the collapse rules: a layer that IS one glob or one project takes that place's key, so
    // a rule naming the layer and a rule naming its definition draw one node. Anything else is a place of
    // the layer's own — nested inside what it refines, holding what it unions — or no place at all.
    private static LawPlace? FromLayer(LayerNoun layer, IReadOnlyList<LayerDefinition> layers)
    {
        // The glob form, and the definition that names a namespace region: Globs carries the region, so
        // both take the glob place and collapse with a rule naming the glob itself.
        if (layer.Definition is not { } definition || layer.Globs.Count > 0) return FromGlobs(layer);

        if (definition is UnionSelection union) return FromUnion(layer, union, layers);

        if (LayerNoun.ProjectOf(definition) is { } project) return FromProject(project, layers);

        // A refinement of a place — most often another layer's cone — is its own box drawn inside the
        // place it refines. Structural, because nesting everywhere else is glob implication, and neither a
        // project nor a refinement has globs to imply anything with.
        LawPlace? parent = NounPlace(definition.Noun, layers);
        return parent is null
            ? null
            : new LawPlace("layer:" + layer.Name, layer.Name, layer.Name, [], true) { Parent = parent };
    }

    private static LawPlace FromGlobs(LayerNoun layer)
    {
        string key = layer.Globs.Count == 1 ? layer.Globs[0] : "layer:" + layer.Name;
        return new LawPlace(key, layer.Name, layer.Name, layer.Globs, true);
    }

    // An adjective-free union of places is the box its operands sit in: it covers no region of its own, and
    // each operand's place is drawn inside it. A union carrying adjectives, or one holding an operand this
    // classifier cannot place, is not a place at all — its rules join the compact list rather than drawing a
    // box that is missing part of itself.
    private static LawPlace? FromUnion(LayerNoun layer, UnionSelection union, IReadOnlyList<LayerDefinition> layers)
    {
        if (union.Adjectives.Count > 0) return null;

        var children = new List<LawPlace>(union.Parts.Count);
        foreach (Selection part in union.Parts)
        {
            LawPlace? child = PlaceOf(part, layers, PlacePosition.Subject);
            if (child is null) return null;

            children.Add(child);
        }

        return new LawPlace("layer:" + layer.Name, layer.Name, layer.Name, [], true) { StructuralChildren = children };
    }

    // The project twin of FromGlob's identity collapse: a layer defined as exactly this project IS the
    // project, so a rule naming the project and a rule naming the layer draw one node under the layer's
    // name. A definition that narrows the project is a subset no project equals, so it never collapses.
    private static LawPlace FromProject(string name, IReadOnlyList<LayerDefinition> layers)
    {
        LayerDefinition? owner = layers.FirstOrDefault(layer => string.Equals(LayerNoun.ProjectOf(layer.Definition), name, StringComparison.Ordinal));
        string label = owner is null ? name : owner.Name;
        return new LawPlace("project:" + name, label, label, [], owner is not null);
    }

    // A type node stands alone on the fence with no sentence around it, so it carries the full name: an
    // `Environment` node would leave the reader guessing which one.
    private static LawPlace FromType(TypeAnchor anchor)
    {
        string name = Display(anchor);
        return new LawPlace(TypeKey(anchor), name, name, [], false);
    }

    // Bare `arch.Types` is the whole solution and places nothing. Whether a narrowing names a region is the
    // model's call (LayerNoun.RegionOf), so the drawing and a layer's payload cannot disagree about it.
    private static LawPlace? FromTypes(Selection selection, IReadOnlyList<LayerDefinition> layers)
    {
        IReadOnlyList<string> region = LayerNoun.RegionOf(selection);
        return region.Count == 1 ? FromGlob(region[0], layers) : null;
    }

    private static string TypeKey(TypeAnchor anchor)
    {
        return "type:" + Display(anchor);
    }

    // The extraction-format name where the type admits one; a pointer or an open construction has no
    // source-level form, and its own simple name is the fallback rather than a refusal out of a
    // renderer.
    private static string Display(TypeAnchor anchor)
    {
        return anchor.FullName ?? anchor.SimpleDisplay;
    }

    // The subtree operator is noise in an identifier, and the prefix is what a reader recognizes:
    // `Microsoft.Build.*` slugs from `Microsoft.Build`. The operator is decomposed by the matcher that
    // owns it, so a second spelling of it could never leave the slug reading the old one.
    private static string IdSourceOf(string glob)
    {
        _ = NamespacePattern.TryParseSubtree(glob, out string prefix);
        return prefix;
    }

    /// <summary>What one drawable verb means to the drawing.</summary>
    internal sealed class DrawableVerb(bool inbound, bool only, string? verbWord)
    {
        /// <summary>Whether the operand is the source of the arrow: the passive voice of a direction verb.</summary>
        internal bool Inbound { get; } = inbound;

        /// <summary>Whether the verb names an allow-list rather than a ban.</summary>
        internal bool Only { get; } = only;

        /// <summary>The word the edge label carries, or null for the plain ban that needs none.</summary>
        internal string? VerbWord { get; } = verbWord;
    }
}
