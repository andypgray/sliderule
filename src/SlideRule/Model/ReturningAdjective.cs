using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Returning(typeof(Task), ...)</c> on a <see cref="MethodSelection" /> → " returning `Task`"
///     (GRAMMAR §4.6, §5.7).
/// </summary>
/// <remarks>
///     The fragment follows the kind-plural and switches the member subject's join to " declared in", so it
///     never reads as narrowing the types (GRAMMAR §6). A closed-generic <c>typeof</c> anchor is refused at spec
///     build (GRAMMAR §8 item 14).
/// </remarks>
internal sealed class ReturningAdjective(IReadOnlyList<TypeAnchor> anchors) : MemberAdjective
{
    /// <summary>The return-type anchors, in authoring order.</summary>
    internal IReadOnlyList<TypeAnchor> Anchors { get; } = anchors;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => " returning " + ProseFormat.AnchorList(Anchors);
}
