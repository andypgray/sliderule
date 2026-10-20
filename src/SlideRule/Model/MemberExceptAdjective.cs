using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.Except(...)</c> → ", except `Main` methods of types named `Program`" (GRAMMAR §5.7, §6), the
///     member twin of <see cref="ExceptAdjective" />, several payloads or-joined.
/// </summary>
/// <remarks>
///     It holds the payload list itself, the member stratum having no union node (GRAMMAR §11): several payloads
///     remove what any one of them names.
/// </remarks>
internal sealed class MemberExceptAdjective(IReadOnlyList<MemberSelection> payloads) : MemberAdjective
{
    /// <summary>The excluded member selections, in authoring order.</summary>
    internal IReadOnlyList<MemberSelection> Payloads { get; } = payloads;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override bool OpensParenthetical => true;

    internal override string Fragment => ", except " + Payloads.ReferenceListProse();
}
