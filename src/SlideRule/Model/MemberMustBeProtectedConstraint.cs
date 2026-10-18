using SlideRule.Fluent;

namespace SlideRule.Model;

/// <summary>
///     <c>.Fields.MustBeProtected()</c> → "must be protected" (GRAMMAR §5.7). Member-only vocabulary, like
///     <see cref="MemberMustBePrivateConstraint" />, and exact like the other accessibility verbs:
///     <c>protected internal</c> and <c>private protected</c> are levels of their own and fail it.
/// </summary>
internal sealed class MemberMustBeProtectedConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be protected";
}
