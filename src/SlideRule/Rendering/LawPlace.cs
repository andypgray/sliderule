namespace SlideRule.Rendering;

/// <summary>
///     One node of the law diagram: a place the spec names, identified by a
///     <see cref="Key" /> so two rules naming the same place draw one node.
/// </summary>
/// <remarks>
///     The mutable flags are accumulated as the rules are walked: a place is not yet known to be a
///     subject, a quarantined scope, or a child when the first rule mints it.
/// </remarks>
internal sealed class LawPlace
{
    internal LawPlace(string key, string label, string idSource, IReadOnlyList<string> globs, bool declaredLayer)
    {
        Key = key;
        BaseLabel = label;
        IdSource = idSource;
        Globs = globs;
        IsDeclaredLayer = declaredLayer;
    }

    internal string Key { get; }

    /// <summary>The name the node ID is slugged from; distinct from the label so an ID stays readable.</summary>
    internal string IdSource { get; }

    /// <summary>The namespace globs this place covers; empty for a project or a type.</summary>
    internal IReadOnlyList<string> Globs { get; }

    internal bool IsDeclaredLayer { get; }

    internal bool IsSubject { get; set; }

    /// <summary>Whether this place is a quarantined scope's sanctioned surface type.</summary>
    internal bool IsFacade { get; set; }

    /// <summary>The scope this place is the quarantined interior of, or null.</summary>
    internal string? QuarantineScopeId { get; set; }

    internal LawPlace? Parent { get; set; }

    /// <summary>
    ///     The places this one contains by construction rather than by namespace containment: the operands
    ///     of a layer defined as a union of places.
    /// </summary>
    /// <remarks>
    ///     Carried here rather than resolved at classification because a child has to be the registered
    ///     instance of its place to be drawn inside anything; <see cref="LawPlaces" /> is where that identity
    ///     lives.
    /// </remarks>
    internal IReadOnlyList<LawPlace> StructuralChildren { get; set; } = [];

    internal string Label => QuarantineScopeId is { } scopeId ? "Quarantine: " + scopeId : BaseLabel;

    private string BaseLabel { get; }
}
