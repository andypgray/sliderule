using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustOnlyReferenceItself()</c> → "must reference only itself (external packages are not
///     constrained by this rule)" (GRAMMAR §5.3). The leaf form of
///     <see cref="MustOnlyReferenceConstraint" />: a graph leaf allows the refined subject and nothing
///     else, which under §4.1's implicit self-allowance leaves the operand list with nothing to hold.
/// </summary>
/// <remarks>
///     Derives from <see cref="Constraint" /> directly rather than from <see cref="OperandConstraint" />: its
///     operand list is empty by construction, and the base's empty <see cref="Constraint.Operands" /> is what
///     every generic walk and the diagram's zero-operand path read. The caveat is
///     <see cref="MustOnlyReferenceConstraint.ExternalCaveat" />, and the self phrase is
///     <see cref="SentenceRenderer.SelfReference" />'s.
/// </remarks>
internal sealed class MustOnlyReferenceItselfConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase =>
        $"must reference only {SentenceRenderer.SelfReference(Subject!)}{MustOnlyReferenceConstraint.ExternalCaveat}";
}
