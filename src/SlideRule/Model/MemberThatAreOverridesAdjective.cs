namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.ThatAreOverrides()</c> premodifies the member head: "overriding methods of types" (GRAMMAR §5.7,
///     §6).
/// </summary>
internal sealed class MemberThatAreOverridesAdjective : MemberAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "overriding ";
}
