namespace SlideRule.Model;

/// <summary>
///     <c>.Authored()</c> premodifies the subject head: "authored types", "authored interfaces"
///     (GRAMMAR §5.2, §6). It narrows to the types no source generator emitted — the opt-out from the
///     §4.1 boundary that puts generator output inside a project noun.
/// </summary>
internal sealed class AuthoredAdjective : SelectionAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "authored ";
}
