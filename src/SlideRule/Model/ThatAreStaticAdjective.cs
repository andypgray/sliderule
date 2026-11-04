namespace SlideRule.Model;

/// <summary>
///     <c>.ThatAreStatic()</c> premodifies the subject head: "static types", "static classes" (GRAMMAR §5.2, §6).
/// </summary>
internal sealed class ThatAreStaticAdjective : SelectionAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "static ";
}
