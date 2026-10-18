using SlideRule.Fluent;
using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.Except(...)</c> → ", except `Main` methods of types named `Program`", canonicalized to
///     sentence-final and after any member <c>Where</c> (GRAMMAR §5.7, §6). The member twin of
///     <see cref="ExceptAdjective" />: each payload renders in reference position, several or-joined.
/// </summary>
/// <remarks>
///     <para>
///         It holds the payload list itself rather than a union of them, because the member stratum has no
///         union node (GRAMMAR §11 declines one): several payloads remove what any one of them names, and the
///         sentence or-joins them the way a type-side union operand list reads.
///     </para>
///     <para>
///         The fragment opens a parenthetical and never closes it, on <see cref="ExceptAdjective" />'s terms:
///         only the junction that follows knows whether text does.
///     </para>
/// </remarks>
internal sealed class MemberExceptAdjective(IReadOnlyList<MemberSelection> payloads) : MemberAdjective
{
    /// <summary>The excluded member selections, in authoring order.</summary>
    internal IReadOnlyList<MemberSelection> Payloads { get; } = payloads;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override bool OpensParenthetical => true;

    internal override string Fragment => ", except " + SentenceRenderer.MemberReferenceList(Payloads);
}
