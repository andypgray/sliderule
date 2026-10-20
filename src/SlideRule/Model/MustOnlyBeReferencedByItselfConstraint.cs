using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustOnlyBeReferencedByItself()</c> → "must be referenced only by itself" (GRAMMAR §5.3). The
///     inbound twin of <see cref="MustOnlyReferenceItselfConstraint" />: on a plain subject the allowed
///     sources are the refined subject — a hermetic set — and on a family subject (§5.1) each cell as
///     declared, which is the modular-monolith law "each module's internals are reached only through its
///     own surface".
/// </summary>
/// <remarks>
///     Derives from <see cref="Constraint" /> directly, as its outbound twin does, and like
///     <see cref="MustOnlyBeReferencedByConstraint" /> needs no external-packages caveat. The self phrase is
///     <see cref="SentenceRenderer.SelfReference" />'s.
/// </remarks>
internal sealed class MustOnlyBeReferencedByItselfConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => $"must be referenced only by {SentenceRenderer.SelfReference(Subject!)}";
}
