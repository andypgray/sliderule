namespace SlideRule.Model;

/// <summary>
///     <c>.ThatAreAbstract()</c> premodifies the subject head: "abstract types", "abstract classes"
///     (GRAMMAR §5.2, §6).
/// </summary>
internal sealed class ThatAreAbstractAdjective : SelectionAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "abstract ";
}
