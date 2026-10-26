using SlideRule.Hosting;

namespace SlideRule.Rendering;

/// <summary>
///     The set of places one law drawing names, in first-seen order, plus the nesting between them.
///     Registration deduplicates on <see cref="LawPlace.Key" />, so the same place named by four rules is
///     one node that accumulates all four rules' facts.
/// </summary>
/// <remarks>
///     Nesting is namespace containment for everything the spec does not state outright: a place is drawn
///     inside the most specific other place whose glob set strictly covers its own. When two candidate
///     parents are incomparable — neither covers the other — there is no "most specific" answer, and the
///     node stays flat rather than picking one and implying a hierarchy the spec never declared. A layer
///     whose definition refines or unions other places arrives already nested, and so does a region a rule
///     names locatively (<c>layer.InNamespace(glob)</c>), because in both the containment is declared
///     rather than inferred — which is the only way to reach it when the container is a project place,
///     since a project place has no globs to infer anything from.
/// </remarks>
internal sealed class LawPlaces(IReadOnlyList<LayerDefinition> layers)
{
    private readonly Dictionary<string, LawPlace> _byKey = new(StringComparer.Ordinal);

    private readonly List<LawPlace> _ordered = [];

    internal IReadOnlyList<LawPlace> Ordered => _ordered;

    internal IReadOnlyList<LawPlace> Roots => _ordered.Where(place => place.Parent is null).ToList();

    internal LawPlace? Subject(Selection? selection)
    {
        LawPlace? place = Register(LawPlaceClassifier.SubjectPlace(selection, layers));
        if (place is not null) place.IsSubject = true;

        return place;
    }

    internal LawPlace? Operand(Selection? selection)
    {
        return Register(LawPlaceClassifier.OperandPlace(selection, layers));
    }

    internal LawPlace? Scope(Selection? selection, string scopeId)
    {
        LawPlace? place = Subject(selection);
        if (place is not null) place.QuarantineScopeId = scopeId;

        return place;
    }

    /// <summary>
    ///     A region named in facade position keeps its own place and gains the facade standing, so a namespace
    ///     two rules name is still one node. A facade that is the scope or covers it has no box inside the scope
    ///     to draw, so it is unplaceable, and registers nothing.
    /// </summary>
    internal LawPlace? Facade(Selection? selection, LawPlace scope)
    {
        LawPlace? candidate = LawPlaceClassifier.FacadePlace(selection, layers);
        if (candidate is null || IsOrCovers(candidate, scope)) return null;

        LawPlace place = Registered(candidate);
        place.IsFacade = true;
        place.Parent ??= scope;
        return place;
    }

    internal IReadOnlyList<LawPlace> Children(LawPlace place)
    {
        return _ordered.Where(candidate => ReferenceEquals(candidate.Parent, place)).ToList();
    }

    /// <summary>
    ///     Assigns each unparented place its most specific strict container, once every place is known.
    ///     Containment is a strict partial order, so the parent chains it builds always ascend and can
    ///     never close a cycle.
    /// </summary>
    internal void ResolveNesting()
    {
        foreach (LawPlace place in _ordered)
        {
            if (place.Parent is not null || place.Globs.Count == 0) continue;

            List<LawPlace> containers = _ordered
                .Where(other => !ReferenceEquals(other, place) && other.Globs.Count > 0 && StrictlyContains(other, place))
                .ToList();

            // The most specific container is the one every other container also covers.
            place.Parent = containers.FirstOrDefault(candidate => containers.All(other => ReferenceEquals(other, candidate) || Contains(other, candidate)));
        }
    }

    private static bool IsOrCovers(LawPlace candidate, LawPlace place)
    {
        if (candidate.Key == place.Key) return true;

        return candidate.Globs.Count > 0 && place.Globs.Count > 0 && Contains(candidate, place);
    }

    private static bool StrictlyContains(LawPlace outer, LawPlace inner)
    {
        return Contains(outer, inner) && !Contains(inner, outer);
    }

    private static bool Contains(LawPlace outer, LawPlace inner)
    {
        return inner.Globs.All(innerGlob => outer.Globs.Any(outerGlob => NamespaceContainment.Implies(innerGlob, outerGlob)));
    }

    private LawPlace? Register(LawPlace? place)
    {
        return place is null ? null : Registered(place);
    }

    // Registration proper, and the one place structural containment is resolved to registered instances: a
    // place is drawn inside another only if the two nodes on the page are the ones the rules registered.
    private LawPlace Registered(LawPlace place)
    {
        if (_byKey.TryGetValue(place.Key, out LawPlace existing)) return existing;

        // A structural parent is drawn whether or not a rule names it — a layer defined as a refinement of
        // another says where it sits, and the box has to be on the page for the child to sit inside it.
        // Registering it first also puts a container ahead of its content in registration order.
        if (place.Parent is { } parent) place.Parent = Registered(parent);

        _byKey.Add(place.Key, place);
        _ordered.Add(place);

        // A structural child keeps a parent it already has, so a place two definitions claim is drawn once,
        // inside the first box that claimed it — the same first-wins rule registration itself follows.
        foreach (LawPlace child in place.StructuralChildren) Registered(child).Parent ??= place;

        return place;
    }
}
