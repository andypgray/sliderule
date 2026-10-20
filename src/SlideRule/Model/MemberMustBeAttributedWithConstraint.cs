using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.MustBeAttributedWith(typeof(McpServerToolAttribute))</c> → "must be attributed with
///     `[McpServerTool]`" (GRAMMAR §5.7), the verb phrase of <see cref="MustBeAttributedWithConstraint" />
///     reused verbatim, since verb position is unambiguous.
/// </summary>
internal sealed class MemberMustBeAttributedWithConstraint(MemberSelection subject, TypeAnchor anchor) : MemberConstraint(subject)
{
    internal TypeAnchor Anchor { get; } = anchor;

    internal override string VerbPhrase => "must be attributed with " + Anchor.BacktickedAttributeName();
}
