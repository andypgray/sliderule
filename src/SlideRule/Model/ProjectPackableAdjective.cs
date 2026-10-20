namespace SlideRule.Model;

/// <summary>
///     <c>.Packable()</c> premodifies the subject head: "packable projects", "packable project `A`"
///     (GRAMMAR §4.10, §6). It narrows to the projects an evaluation reported as producing a package.
/// </summary>
/// <remarks>
///     Known-true only: a project whose <c>IsPackable</c> was never evaluated is not admitted, since unknown is
///     no claim either way. The packaging verbs hold the same rule from the other side and pass it.
/// </remarks>
internal sealed class ProjectPackableAdjective : ProjectAdjective
{
    internal override AdjectivePlacement Placement => AdjectivePlacement.HeadPrefix;

    internal override string Fragment => "packable ";
}
