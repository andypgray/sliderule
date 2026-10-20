using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary><c>.Except(projects)</c> → ", except {reference}" (GRAMMAR §4.10, §6).</summary>
internal sealed class ProjectExceptAdjective(ProjectSelection payload) : ProjectAdjective
{
    internal ProjectSelection Payload { get; } = payload;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override bool OpensParenthetical => true;

    internal override string Fragment => ", except " + Payload.ReferenceProse();
}
