using SlideRule.Codebase;
using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>
///     The names of a member selection's own multi-name <c>Named</c> lists that none of its members carries
///     (GRAMMAR §4.6): the member twin of <see cref="DeadNames" />, read on a rule's member subject as one
///     empty operand per name.
/// </summary>
/// <remarks>
///     <para>
///         The resolved member list is the whole test, on the type side's argument. <c>Named</c> keeps the
///         members carrying a listed name and every other member adjective only filters further, one member
///         at a time, so a name no resolved member carries is exactly a one-name spelling that matches
///         nothing.
///     </para>
///     <para>
///         A one-name list has no arm here, since its name is dead exactly when the subject is. A
///         <c>Named</c> inside an <c>Except</c> payload is not read: it exempts, and a dead exemption narrows
///         nothing. Nor is a <c>Named</c> on the source type selection, which the type-side gate has already
///         read by the time the members are resolved.
///     </para>
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
