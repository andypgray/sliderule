namespace SlideRule.Model;

/// <summary>
///     <c>.ThatAreSealed()</c> premodifies the subject head: "sealed types", "sealed classes" (GRAMMAR §5.2, §6).
/// </summary>
internal sealed class ThatAreSealedAdjective : SelectionAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "sealed ";
}
