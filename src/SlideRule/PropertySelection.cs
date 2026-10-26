using SlideRule.Model;

namespace SlideRule;

/// <summary>
///     The properties the selected types declare, reached with <c>.Properties</c> on a
///     <see cref="Selection" />. Narrow it with the member adjectives (<c>WithSuffix</c>,
///     <c>WithPrefix</c>, <c>WithNameMatching</c>, <c>Named</c>, <c>AttributedWith</c>, <c>ThatAreStatic</c>, the
///     accessibility adjectives such as <c>ThatArePublic</c>, <c>Where</c>), each of which hands back a
///     property selection again, so the property-only call
///     stays reachable whatever the order; finish it with a member verb such as <c>MustHaveSuffix</c>
///     or <c>MustBePublic</c>, or with <c>MustBeGetOnly</c>, which properties alone accept.
///     <c>MustBeGetOnly</c> reads the declaration strictly: a property passes only when it declares no
///     setter at all, so a <c>private set</c> and an <c>init</c> both fail it. Immutable and reusable:
///     every call hands back a new selection and leaves this one as it was.
/// </summary>
public sealed class PropertySelection : MemberSelection
{
    internal PropertySelection(Selection source, IReadOnlyList<MemberAdjective> adjectives)
        : base(source, MemberKindFilter.Property, adjectives)
    {
    }

    internal override MemberSelection WithAdjectives(IReadOnlyList<MemberAdjective> adjectives)
    {
        return new PropertySelection(Source, adjectives);
    }
}
