using SlideRule.Fluent;

namespace SlideRule.Model;

/// <summary>
///     <c>.Methods.MustBeOverridable()</c> → "must be overridable" (GRAMMAR §5.7). Member-only vocabulary:
///     the wider twin of <see cref="MemberMustBeVirtualConstraint" />, passing a member a derived type can
///     still override — declared <c>virtual</c> or <c>abstract</c>, or an <c>override</c> not declared
///     <c>sealed</c> — so an override that keeps its chain open does not red as "not virtual".
/// </summary>
/// <remarks>
///     It reads the member's own declaration flags and nothing about its declaring type, so an unsealed
///     override inside a sealed class passes: the declaration is overridable, and whether the class can be
///     derived from is a type-side question <c>MustBeSealed</c> already asks.
/// </remarks>
internal sealed class MemberMustBeOverridableConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be overridable";
}
