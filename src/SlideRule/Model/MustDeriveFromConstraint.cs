using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustDeriveFrom(typeof(ControllerBase))</c> → "must derive from `ControllerBase`" (GRAMMAR §5.3).
/// </summary>
internal sealed class MustDeriveFromConstraint(Selection subject, TypeAnchor anchor) : Constraint(subject)
{
    internal TypeAnchor Anchor { get; } = anchor;

    internal override string VerbPhrase => "must derive from " + Anchor.BacktickedName();
}
