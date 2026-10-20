namespace SlideRule.Model;

/// <summary>
///     <c>.Fields.MustBeReadonly()</c> → "must be readonly" (GRAMMAR §5.7). Fields-only by receiver type.
/// </summary>
/// <remarks>
///     A <c>const</c> field satisfies the verb, const being readonly's superset. The name follows the C#
///     keyword it renders, not <see cref="IMemberInfo.IsReadOnly" />, the fact it reads: a verb mirrors its
///     fragment.
/// </remarks>
internal sealed class MemberMustBeReadonlyConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be readonly";
}
