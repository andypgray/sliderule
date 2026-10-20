using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotReferenceEachOther()</c> → "must not reference the others" (GRAMMAR §5.3). The
///     cross-cell ban over a family subject (§5.1): an owned instance whose far end lies in any other
///     cell, as declared, is a violation. Symmetric, so it has no inbound twin.
/// </summary>
/// <remarks>
///     Derives from <see cref="Constraint" /> directly, as <see cref="MustOnlyReferenceItselfConstraint" />
///     does: the targets are the subject's own cells. Spec build refuses a plain subject (§8 item 28), so the
///     plain reading below is a total-function fallback no built model carries.
/// </remarks>
internal sealed class MustNotReferenceEachOtherConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => "must not reference " + SentenceRenderer.OtherCells(Subject!);
}
