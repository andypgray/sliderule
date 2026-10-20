using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustBeAttributedWith(typeof(ApiControllerAttribute))</c> → "must be attributed with
///     `[ApiController]`" (GRAMMAR §5.3).
/// </summary>
internal sealed class MustBeAttributedWithConstraint(Selection subject, TypeAnchor anchor) : Constraint(subject)
{
    internal TypeAnchor Anchor { get; } = anchor;

    internal override string VerbPhrase => "must be attributed with " + Anchor.BacktickedAttributeName();
}
