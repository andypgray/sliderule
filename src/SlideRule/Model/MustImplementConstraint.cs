using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary><c>.MustImplement(typeof(IDisposable))</c> → "must implement `IDisposable`" (GRAMMAR §5.3).</summary>
internal sealed class MustImplementConstraint(Selection subject, TypeAnchor anchor) : Constraint(subject)
{
    internal TypeAnchor Anchor { get; } = anchor;

    internal override string VerbPhrase => "must implement " + Anchor.BacktickedName();
}
