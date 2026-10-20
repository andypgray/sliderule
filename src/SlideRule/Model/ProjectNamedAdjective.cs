using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Named(name, …)</c> → the head itself: "project `A`" for one name, "projects `A` or `B`" for
///     several (GRAMMAR §4.10, §6). Matching is exact and ordinal.
/// </summary>
internal sealed class ProjectNamedAdjective(IReadOnlyList<string> names) : ProjectAdjective
{
    /// <summary>The exact project names, in authoring order.</summary>
    internal IReadOnlyList<string> Names { get; } = names;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Head;

    internal override string Fragment
    {
        get
        {
            string head = Names.Count == 1 ? "project " : "projects ";
            return head + ProseFormat.BacktickedList(Names);
        }
    }
}
