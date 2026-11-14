using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustCarry(typeof(AuthorizeAttribute))</c> → "must carry `[Authorize]`" (GRAMMAR §5.3): declared on
///     the subject or inherited from a base type.
/// </summary>
internal sealed class MustCarryConstraint(Selection subject, TypeAnchor anchor) : Constraint(subject)
{
    internal TypeAnchor Anchor { get; } = anchor;

    internal override string VerbPhrase => "must carry " + Anchor.BacktickedAttributeName();
}
