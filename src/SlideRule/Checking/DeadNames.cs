using SlideRule.Codebase;
using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>
///     The names of a selection's own multi-name <c>Named</c> lists that none of its members carries (GRAMMAR
///     §4.1): read on an operand or an exemplar part as a dead one, on a subject as an empty one, and dropped
///     from a subject before a run over one project's compilation evaluates it.
/// </summary>
/// <remarks>
///     <para>
///         The membership set is the whole test. <c>Named</c> keeps the types carrying a listed name and every
///         other adjective only filters further, so a name no member carries is exactly a one-name spelling
///         that matches nothing, and dropping it leaves the membership as it was. That holds because every
///         adjective narrows through a test of one type (<c>SelectionEvaluator.Keeper</c>), which has no set to
///         look across.
///     </para>
///     <para>
///         A one-name list has no arm here, since its name is dead exactly when the selection is. A
///         <c>Named</c> inside an <c>Except</c> payload is not read: it exempts, and a dead exemption narrows
///         nothing. Nor is one inside a union's parts, a family's cells or behind a layer's definition, which
///         fold before the selection's own list is read; a caller that wants them read reads each part itself.
///         The pass over a rule's lists opens a union's parts and a layer's definition; the subject gate and the
///         exemplar open a union's parts and leave a layer folded.
///     </para>
/// </remarks>
internal static class DeadNames
{
    /// <summary>
    ///     Each dead name of <paramref name="selection" />'s own lists, as <paramref name="selection" /> spelled
    ///     with that one name, in list order.
    /// </summary>
    internal static IEnumerable<Selection> In(Selection selection, HashSet<TypeNode> members)
    {
        IReadOnlyList<SelectionAdjective> adjectives = selection.Adjectives;
        HashSet<string>? carried = null;
        for (var j = 0; j < adjectives.Count; j++)
        {
            if (adjectives[j] is not NamedAdjective { Names.Count: > 1 } named) continue;

            carried ??= Carried(members);
            foreach (string name in named.Names)
            {
                if (carried.Contains(name)) continue;

                var narrowed = new List<SelectionAdjective>(adjectives);
                narrowed[j] = new NamedAdjective([name]);
                yield return selection.WithRefinements(narrowed);
            }
        }
    }

    /// <summary>Whether <paramref name="selection" /> carries a list of its own that a name could be dead in.</summary>
    internal static bool Possible(Selection selection)
    {
        return selection.Adjectives.Any(adjective => adjective is NamedAdjective { Names.Count: > 1 });
    }

    /// <summary>
    ///     <paramref name="selection" /> with its dead names dropped from their lists, or
    ///     <paramref name="selection" /> itself where none is dead. <paramref name="members" /> must be
    ///     non-empty: then every list keeps at least one name, since each member carries one from each list.
    /// </summary>
    internal static Selection Dropped(Selection selection, HashSet<TypeNode> members)
    {
        IReadOnlyList<SelectionAdjective> adjectives = selection.Adjectives;
        HashSet<string>? carried = null;
        List<SelectionAdjective>? kept = null;
        for (var j = 0; j < adjectives.Count; j++)
        {
            if (adjectives[j] is not NamedAdjective { Names.Count: > 1 } named) continue;

            carried ??= Carried(members);
            List<string> live = named.Names
                .Where(carried.Contains)
                .ToList();
            if (live.Count == named.Names.Count) continue;

            kept ??= new List<SelectionAdjective>(adjectives);
            kept[j] = new NamedAdjective(live);
        }

        return kept is null ? selection : selection.WithRefinements(kept);
    }

    private static HashSet<string> Carried(HashSet<TypeNode> members)
    {
        return new HashSet<string>(members.Select(type => type.Name), StringComparer.Ordinal);
    }
}
