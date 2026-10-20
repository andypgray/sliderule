namespace SlideRule.Model;

/// <summary>
///     The member selection the <c>.Members</c> and <c>.Events</c> projections mint (GRAMMAR §4.6), which have
///     no kind-only verbs to bind.
/// </summary>
internal sealed class KindMemberSelection : MemberSelection
{
    internal KindMemberSelection(Selection source, MemberKindFilter kind, IReadOnlyList<MemberAdjective> adjectives)
        : base(source, kind, adjectives)
    {
    }

    private protected override MemberSelection Rebuild(IReadOnlyList<MemberAdjective> adjectives)
    {
        return new KindMemberSelection(Source, Kind, adjectives);
    }
}
