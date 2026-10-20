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

            carried ??= new HashSet<string>(members.Select(member => member.Name), StringComparer.Ordinal);
            foreach (string name in named.Names)
            {
                if (carried.Contains(name)) continue;

                var narrowed = new List<MemberAdjective>(adjectives);
                narrowed[j] = new MemberNamedAdjective([name]);
                yield return selection.WithAdjectives(narrowed);
            }
        }
    }
}
