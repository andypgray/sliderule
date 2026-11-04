namespace SlideRule.Model;

/// <summary>
///     <c>.ThatAreRecords()</c> premodifies the subject head: "record types", "record classes" (GRAMMAR §5.2, §6).
/// </summary>
internal sealed class ThatAreRecordsAdjective : SelectionAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "record ";
}
