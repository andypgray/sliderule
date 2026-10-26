using SlideRule.Codebase;
using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>
///     The names of a member selection's own multi-name <c>Named</c> lists that none of its members carries
///     (GRAMMAR §4.6): the member twin of <see cref="DeadNames" />.
/// </summary>
/// <remarks>
///     The resolved member list is the whole test, on <see cref="DeadNames" />'s argument: <c>Named</c> keeps the
///     members carrying a listed name, and every other member adjective only filters further, one member at a
///     time. As there, a one-name list and a <c>Named</c> inside an <c>Except</c> payload are not read. Nor is a
///     <c>Named</c> on the source type selection, which the type-side gate has already read by the time the
///     members are resolved.
/// </remarks>
internal static class MemberDeadNames
{
    /// <summary>
    ///     Each dead name of <paramref name="selection" />'s own lists, as <paramref name="selection" /> spelled
    ///     with that one name, in list order.
    /// </summary>
    internal static IEnumerable<MemberSelection> In(MemberSelection selection, IReadOnlyList<MemberNode> members)
    {
        IReadOnlyList<MemberAdjective> adjectives = selection.Adjectives;
        HashSet<string>? carried = null;
        for (var j = 0; j < adjectives.Count; j++)
        {
            if (adjectives[j] is not MemberNamedAdjective { Names.Count: > 1 } named) continue;

            carried ??= Carried(members);
            foreach (string name in named.Names)
            {
                if (carried.Contains(name)) continue;

                var narrowed = new List<MemberAdjective>(adjectives);
                narrowed[j] = new MemberNamedAdjective([name]);
                yield return selection.WithAdjectives(narrowed);
            }
        }
    }

    /// <summary>Whether <paramref name="selection" /> carries a list of its own that a name could be dead in.</summary>
    internal static bool Possible(MemberSelection selection)
    {
        return selection.Adjectives.Any(adjective => adjective is MemberNamedAdjective { Names.Count: > 1 });
    }

    /// <summary>
    ///     <paramref name="selection" /> with its dead names dropped from their lists, or <paramref name="selection" />
    ///     itself where none is dead. <paramref name="members" /> must be non-empty: then every list keeps at least
    ///     one name, since each member carries one from each list.
    /// </summary>
    internal static MemberSelection Dropped(MemberSelection selection, IReadOnlyList<MemberNode> members)
    {
        IReadOnlyList<MemberAdjective> adjectives = selection.Adjectives;
        HashSet<string>? carried = null;
        List<MemberAdjective>? kept = null;
        for (var j = 0; j < adjectives.Count; j++)
        {
            if (adjectives[j] is not MemberNamedAdjective { Names.Count: > 1 } named) continue;

            carried ??= Carried(members);
            List<string> live = named.Names
                .Where(carried.Contains)
                .ToList();
            if (live.Count == named.Names.Count) continue;

            kept ??= new List<MemberAdjective>(adjectives);
            kept[j] = new MemberNamedAdjective(live);
        }

        return kept is null ? selection : selection.WithAdjectives(kept);
    }

    private static HashSet<string> Carried(IReadOnlyList<MemberNode> members)
    {
        return new HashSet<string>(members.Select(member => member.Name), StringComparer.Ordinal);
    }
}
