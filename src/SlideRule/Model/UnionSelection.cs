namespace SlideRule.Model;

/// <summary>
///     The union of one or more selections, <c>arch.AnyOf(a, b, …)</c> (GRAMMAR §5.1). A sequence overload can
///     mint one with no parts, which the validator refuses when the spec loads (§8 item 36).
/// </summary>
/// <remarks>
///     Adjectives apply to the union, not through it: <c>AnyOf(a, b).Except(c)</c> is <c>(a ∪ b) − c</c>, so
///     the union carries its own adjective list and the evaluator applies it after the set union. A union has
///     no single noun, so <see cref="Noun" /> is null and the renderer gives it an arm of its own (§6).
/// </remarks>
internal sealed class UnionSelection : Selection
{
    internal UnionSelection(Arch owner, IReadOnlyList<Selection> parts)
        : this(owner, parts, Array.Empty<SelectionAdjective>())
    {
    }

    private UnionSelection(Arch owner, IReadOnlyList<Selection> parts, IReadOnlyList<SelectionAdjective> adjectives)
        : base(owner)
    {
        Parts = parts;
        Adjectives = adjectives;
    }

    /// <summary>The unioned selections, in order (named <c>Parts</c> to stay clear of the <c>.Members</c> projection, §4.6).</summary>
    internal IReadOnlyList<Selection> Parts { get; }

    internal override SelectionNoun? Noun => null;

    internal override IReadOnlyList<SelectionAdjective> Adjectives { get; }

    /// <summary>
    ///     Mints a union, flattening nested unions at mint so prose and evaluation both read one leaf
    ///     list: <c>AnyOf(AnyOf(a, b), c)</c> ≡ <c>AnyOf(a, b, c)</c>. Only an <em>adjective-free</em>
    ///     union operand flattens — an inner union carrying adjectives is a narrowed set of its own, so
    ///     it stays a leaf. So does an <em>empty</em> one: flattened, it would vanish into its parent, and
    ///     <c>AnyOf(AnyOf(empty), b)</c> would load as <c>AnyOf(b)</c> with nothing left for the validator
    ///     to report.
    /// </summary>
    internal static UnionSelection Create(Arch owner, IReadOnlyList<Selection> parts)
    {
        var flattened = new List<Selection>(parts.Count);
        foreach (Selection part in parts)
            if (part is UnionSelection { Adjectives.Count: 0, Parts.Count: > 0 } nested) flattened.AddRange(nested.Parts);
            else flattened.Add(part);

        return new UnionSelection(owner, flattened);
    }

    internal override Selection WithRefinements(IReadOnlyList<SelectionAdjective> adjectives)
    {
        return new UnionSelection(Owner, Parts, adjectives);
    }
}
