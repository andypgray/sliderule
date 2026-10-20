namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.MustBeOverridable()</c> → "must be overridable" (GRAMMAR §5.7), the wider twin of
///     <see cref="MemberMustBeVirtualConstraint" />: a <c>virtual</c> or <c>abstract</c> member, or an
///     <c>override</c> not declared <c>sealed</c>, passes. Only the member's own declaration is read.
/// </summary>
internal sealed class MemberMustBeOverridableConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be overridable";
}
