namespace SlideRule.Model;

/// <summary>
///     <c>.Fields.ThatAreStatic()</c> premodifies the member head: "static fields of types" (GRAMMAR §5.7, §6).
/// </summary>
internal sealed class MemberThatAreStaticAdjective : MemberAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "static ";
}
