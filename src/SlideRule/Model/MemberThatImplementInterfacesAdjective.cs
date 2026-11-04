namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.ThatImplementInterfaces()</c> premodifies the member head: "interface-implementing methods of
///     types" (GRAMMAR §5.7, §6).
/// </summary>
internal sealed class MemberThatImplementInterfacesAdjective : MemberAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "interface-implementing ";
}
