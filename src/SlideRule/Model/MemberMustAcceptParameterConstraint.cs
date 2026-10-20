using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.MustAcceptParameter(typeof(CancellationToken))</c> → "must accept a parameter of type
///     `CancellationToken`" (GRAMMAR §5.7, §4.6), a phrasing safe for any type name where "a/an `X` parameter"
///     is not. A closed-generic <c>typeof</c> anchor is refused at spec build (GRAMMAR §8 item 20).
/// </summary>
internal sealed class MemberMustAcceptParameterConstraint(MemberSelection subject, TypeAnchor anchor) : MemberConstraint(subject)
{
    /// <summary>The parameter-type anchor, matched definition-level (GRAMMAR §4.6).</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override string VerbPhrase =>
        "must accept a parameter of type " + Anchor.BacktickedName();
}
