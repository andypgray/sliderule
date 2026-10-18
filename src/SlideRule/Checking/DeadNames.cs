using SlideRule.Codebase;
using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>
///     The names of a selection's own multi-name <c>Named</c> lists that none of its members carries, and the globs
///     of its own multi-glob <c>Under</c> lists that none of its members is declared under (GRAMMAR §4.1): read on an
///     operand or an exemplar part as a dead one, on a subject as an empty one, and dropped from a subject before a
///     run over one project's compilation evaluates it.
/// </summary>
/// <remarks>
///     <para>
///         The membership set is the whole test. <c>Named</c> keeps the types carrying a listed name, <c>Under</c>
///         the types declared under a listed glob, and every other adjective only filters further, so a name no
///         member carries, or a glob no member is under, is exactly a one-entry spelling that matches nothing, and
///         dropping it leaves the membership as it was. That holds because every adjective narrows through a test
///         of one type (<c>SelectionEvaluator.Keeper</c>), which has no set to look across.
///     </para>
///     <para>
///         A one-entry list has no arm here, since its entry is dead exactly when the selection is. A list inside
///         an <c>Except</c> payload is not read: it exempts, and a dead exemption narrows nothing. Nor is one inside
///         a union's parts, a family's cells or behind a layer's definition, which fold before the selection's own
///         list is read; a caller that wants them read reads each part itself. The pass over a rule's lists opens a
///         union's parts and a layer's definition; the subject gate and the exemplar open a union's parts and leave
///         a layer folded.
///     </para>
/// </remarks>
internal static class DeadNames
{
    /// <summary>
    ///     Each dead name or glob of <paramref name="selection" />'s own lists, as <paramref name="selection" />
    ///     spelled with that one entry, in list order.
    /// </summary>
    internal static IEnumerable<Selection> In(
        Selection selection, HashSet<TypeNode> members, SelectionEvaluator selections)
    {
        IReadOnlyList<SelectionAdjective> adjectives = selection.Adjectives;
        HashSet<string>? carried = null;
        for (var j = 0; j < adjectives.Count; j++)
            switch (adjectives[j])
            {
                case NamedAdjective { Names.Count: > 1 } named:
                    carried ??= Carried(members);
                    foreach (string name in named.Names)
                        if (!carried.Contains(name))
                            yield return Spelled(selection, j, new NamedAdjective([name]));

                    break;
                case UnderAdjective { Globs.Count: > 1 } under:
                    foreach (string glob in under.Globs)
                        if (!AnyUnder(members, glob, selections))
                            yield return Spelled(selection, j, new UnderAdjective([glob]));

                    break;
            }
    }

    /// <summary>Whether <paramref name="selection" /> carries a list of its own that an entry could be dead in.</summary>
    internal static bool Possible(Selection selection)
    {
        return selection.Adjectives.Any(adjective =>
            adjective is NamedAdjective { Names.Count: > 1 } or UnderAdjective { Globs.Count: > 1 });
    }

    /// <summary>
    ///     <paramref name="selection" /> with its dead names and globs dropped from their lists, or
    ///     <paramref name="selection" /> itself where none is dead. <paramref name="members" /> must be non-empty:
    ///     then every list keeps at least one entry, since each member satisfies one from each list.
    /// </summary>
    internal static Selection Dropped(Selection selection, HashSet<TypeNode> members, SelectionEvaluator selections)
    {
        IReadOnlyList<SelectionAdjective> adjectives = selection.Adjectives;
        HashSet<string>? carried = null;
        List<SelectionAdjective>? kept = null;
        for (var j = 0; j < adjectives.Count; j++)
        {
            SelectionAdjective? live = adjectives[j] switch
            {
                NamedAdjective { Names.Count: > 1 } named => LiveNames(named, carried ??= Carried(members)),
                UnderAdjective { Globs.Count: > 1 } under => LiveGlobs(under, members, selections),
                _ => null
            };
            if (live is null) continue;

            kept ??= new List<SelectionAdjective>(adjectives);
            kept[j] = live;
        }

        return kept is null ? selection : selection.WithRefinements(kept);
    }

    // The list with its dead names dropped, or null where every name is live.
    private static NamedAdjective? LiveNames(NamedAdjective named, HashSet<string> carried)
    {
        List<string> live = named.Names
            .Where(carried.Contains)
            .ToList();
        return live.Count == named.Names.Count ? null : new NamedAdjective(live);
    }

    // The list with its dead globs dropped, or null where every glob is live.
    private static UnderAdjective? LiveGlobs(
        UnderAdjective under, HashSet<TypeNode> members, SelectionEvaluator selections)
    {
        List<string> live = under.Globs
            .Where(glob => AnyUnder(members, glob, selections))
            .ToList();
        return live.Count == under.Globs.Count ? null : new UnderAdjective(live);
    }

    private static Selection Spelled(Selection selection, int index, SelectionAdjective single)
    {
        var narrowed = new List<SelectionAdjective>(selection.Adjectives) { [index] = single };
        return selection.WithRefinements(narrowed);
    }

    private static bool AnyUnder(HashSet<TypeNode> members, string glob, SelectionEvaluator selections)
    {
        Func<IReadOnlyList<string>, bool> under = SelectionEvaluator.UnderAny([glob]);
        return members.Any(type =>
            type.FilePaths.Any(path => selections.RelativeSegments(path) is { } segments && under(segments)));
    }

    private static HashSet<string> Carried(HashSet<TypeNode> members)
    {
        return new HashSet<string>(members.Select(type => type.Name), StringComparer.Ordinal);
    }
}
