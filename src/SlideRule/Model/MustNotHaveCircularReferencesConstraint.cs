using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotHaveCircularReferences()</c> → "must not have circular references with the others"
///     (GRAMMAR §5.3). The family whose cell graph must carry no circle (§5.1): cells may reference one
///     another, but no strongly connected component of the cell graph may hold more than one cell.
/// </summary>
/// <remarks>
///     Derives from <see cref="Constraint" /> directly, as <see cref="MustOnlyReferenceItselfConstraint" />
///     does: the targets are the subject's own cells. A violation's identity is the type pair on every arrow
///     inside a component (§4.3), never the cycle. Spec build refuses a plain subject and a family of projects
///     (§8 item 29), so the plain reading below is a total-function fallback no built model carries.
/// </remarks>
internal sealed class MustNotHaveCircularReferencesConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase =>
        "must not have circular references with " + SentenceRenderer.OtherCells(Subject!);
}
