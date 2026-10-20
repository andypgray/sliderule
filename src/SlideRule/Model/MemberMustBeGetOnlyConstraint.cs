namespace SlideRule.Model;

/// <summary>
///     <c>.Properties.MustBeGetOnly()</c> → "must be get-only" (GRAMMAR §5.7), properties-only by receiver
///     type. Strict: an <c>init</c> setter is still a setter.
/// </summary>
internal sealed class MemberMustBeGetOnlyConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be get-only";
}
