using SlideRule.Checking;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Model;

namespace SlideRule.Rendering;

/// <summary>
///     Works out which directory each layer's local-rules card belongs in. A layer gets a card when at
///     least one Enforce or Migrate rule has that layer as its subject: narrowing the layer with
///     adjectives or <c>Except</c> still counts, and so does naming it as one cell of a family, but a
///     union of selections does not, having no single home directory, so a union's rules stay in the
///     root block alone. Rules a scope produced are left out, because the scope's own card carries
///     them.
/// </summary>
/// <remarks>
///     <see cref="DirectoryPlacement" /> decides where a card lands. A purpose alone gets no card: a card exists
///     to carry rules.
/// </remarks>
// Scope children are excluded by their payload rather than their posture: a quarantined layer's
// containment subject is layer-anchored, but its story belongs to the scope card and the two
// emission keys must not double-emit — and keying on the payload means a posture added later cannot
// slip through the filter. A caution over a layer would be excluded even without it: its tripwire
// carries no constraint to read a subject from, so it anchors nothing.
internal static class LayerContextResolver
{
    /// <summary>
    ///     Resolves a placement for every layer that has rules of its own, in the order the spec declares the
    ///     layers.
    /// </summary>
    internal static IReadOnlyList<LayerPlacement> Resolve(
        ArchitectureModel model, SelectionEvaluator evaluator, DirectoryPlacement.Boundary boundary)
    {
        var placements = new List<LayerPlacement>();

        foreach (LayerDefinition layer in model.Layers)
        {
            List<ArchRule> anchored = AnchoredRules(model, layer).ToList();
            if (anchored.Count == 0) continue;

            CardDestination destination = DirectoryPlacement.Place(
                evaluator, BareLayer(anchored[0], layer), boundary, "layer", layer.Name);

            placements.Add(new LayerPlacement(layer.Name, layer.Purpose, anchored, destination));
        }

        return placements;
    }

    /// <summary>Reports whether any layer has at least one Enforce or Migrate rule of its own.</summary>
    public static bool HasAnchoredLayers(ArchitectureModel model)
    {
        Guard.NotNull(model, nameof(model));

        return model.Layers.Any(layer => AnchoredRules(model, layer).Any());
    }

    // The rules no scope desugared into whose subject noun head is this layer, in model order.
    private static IEnumerable<ArchRule> AnchoredRules(ArchitectureModel model, LayerDefinition layer)
    {
        return model.Rules.Where(rule => rule.Scope is null && IsAnchoredOn(rule, layer));
    }

    // A family rule places its bullet on every cell's card, because the sentence names every cell and so
    // reads correctly on each (GRAMMAR §5.1, §6). A refinement keeps the same noun, so a refined subject,
    // family or plain, still anchors. The project form of a family anchors nothing, as a bare project noun
    // does not.
    private static bool IsAnchoredOn(ArchRule rule, LayerDefinition layer)
    {
        // Kept as separate conjuncts because the order is the guard: the UnionSelection test has to be
        // read before .Noun, which throws on a union. A merged pattern preserves that order but buries it.
        // ReSharper disable once MergeIntoPattern
        return rule.Constraint?.Subject is { } subject
               && subject is not UnionSelection
               && MatchingCell(subject.Noun, layer.Name) is not null;
    }

    // The whole layer as a bare Selection (its noun, no adjectives), so evaluation ranges over every
    // type in the layer rather than an anchored rule's possibly-refined subject. For a family it is the
    // MATCHING cell rather than the subject's own head, which is what places one family rule's card at
    // each cell's own directory. Its owning Arch is borrowed from the same subject (every selection in
    // one build shares the single Arch, and the evaluator never reads the owner). A plain refined
    // selection rather than a Layer, which would carry a registration nothing declares.
    private static Selection BareLayer(ArchRule anchoredRule, LayerDefinition layer)
    {
        // Both bangs and the third are IsAnchoredOn's: it already read this constraint's subject and
        // matched a cell on it, which no rule with a null constraint and no project-subject rule (whose
        // Subject is null, GRAMMAR §4.10) can do. Only an anchored rule reaches here.
        Selection subject = anchoredRule.Constraint!.Subject!;
        return new RefinedSelection(
            subject.Owner, MatchingCell(subject.Noun, layer.Name)!, Array.Empty<SelectionAdjective>());
    }

    private static LayerNoun? MatchingCell(SelectionNoun noun, string layerName)
    {
        if (noun is LayerNoun head)
            return string.Equals(head.Name, layerName, StringComparison.Ordinal) ? head : null;

        if (noun is not EachNoun { Layers: { } cells }) return null;

        return cells
            .Select(cell => (LayerNoun)cell.Noun)
            .FirstOrDefault(cell => string.Equals(cell.Name, layerName, StringComparison.Ordinal));
    }
}
