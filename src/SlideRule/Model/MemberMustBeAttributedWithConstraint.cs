using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.MustBeAttributedWith(typeof(McpServerToolAttribute))</c> → "must be attributed with
///     `[McpServerTool]`" (GRAMMAR §5.7) — the verb phrase of <see cref="MustBeAttributedWithConstraint" />
///     reused verbatim, because verb position is unambiguous; only the adjective form differs
///     (<see cref="MemberAttributedWithAdjective" />). The anchor is a <see cref="TypeAnchor" />, so the
///     string form renders the identical verb phrase.
/// </summary>
internal sealed class MemberMustBeAttributedWithConstraint(MemberSelection subject, TypeAnchor anchor) : MemberConstraint(subject)
{
    /// <summary>The attribute the subject members must carry, by <c>typeof</c> or by definition name.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override string VerbPhrase => "must be attributed with " + Anchor.BacktickedAttributeName();
}
