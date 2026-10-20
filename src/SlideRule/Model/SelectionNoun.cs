namespace SlideRule.Model;

/// <summary>
///     The head of a <see cref="Selection" /> — a small closed hierarchy so each noun owns its own
///     prose fragments (GRAMMAR §2 admission rule, §6).
/// </summary>
internal abstract class SelectionNoun
{
    /// <summary>The locative phrase appended after the subject head, e.g. <c> in `MyApp.*`</c>.</summary>
    internal abstract string Locative { get; }

    /// <summary>How the bare noun renders as a reference; defaults to the "types" head plus locative.</summary>
    internal virtual string ReferenceFragment => "types" + Locative;

    /// <summary>
    ///     The subject-position head plural, "types" wherever the <see cref="Locative" /> carries the
    ///     distinguishing phrase. A noun whose fragment is its whole head overrides it (GRAMMAR §5.1), so the
    ///     head survives adjectives the way an <c>OfKind</c> substitution does.
    /// </summary>
    internal virtual string SubjectHead => "types";

    /// <summary>
    ///     The hoisted locative of a homogeneous union: the phrase after the one shared head when every operand
    ///     of an <c>arch.AnyOf</c> is this kind of noun, operand names or-joined (GRAMMAR §6), as in
    ///     " in projects `A`, `B` or `C`". Null, the default, declares no collapse, and the union or-joins its
    ///     operands instead. Separate from <see cref="CollapsedReference" /> so the head stays substitutable
    ///     (<c>OfKind</c>) and union-level adjectives still attach to a head.
    /// </summary>
    /// <param name="group">The nouns of the union's operands, in operand order; all of this noun's type.</param>
    internal virtual string? CollapsedLocative(IReadOnlyList<SelectionNoun> group)
    {
        return null;
    }

    /// <summary>
    ///     How a collapsed homogeneous union reads in reference position, the collapsed twin of
    ///     <see cref="ReferenceFragment" />; reached only when <see cref="CollapsedLocative" /> is non-null. A
    ///     noun whose fragment is its whole phrase overrides it.
    /// </summary>
    internal virtual string CollapsedReference(IReadOnlyList<SelectionNoun> group)
    {
        return "types" + CollapsedLocative(group);
    }
}
