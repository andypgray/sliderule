using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary><c>.Except(selection, …)</c> → ", except {reference}" (GRAMMAR §5.2, §6).</summary>
internal sealed class ExceptAdjective(Selection payload) : SelectionAdjective
{
    internal Selection Payload { get; } = payload;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override bool OpensParenthetical => true;

    internal override string Fragment => ", except " + Payload.ReferenceProse();
}
