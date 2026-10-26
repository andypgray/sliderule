using SlideRule.Internal;

namespace SlideRule.Model;

/// <summary>
///     A reified member-shape constraint (GRAMMAR §4.6): a <see cref="MemberSelection" /> subject plus
///     a member modal verb phrase.
/// </summary>
/// <remarks>
///     Its inherited <see cref="Constraint.Subject" /> is the type selection the members are drawn from, so a
///     walk that reaches through the base subject (foreign-<see cref="Arch" /> detection, §8 item 10; Quarantine
///     desugaring, §7) works unchanged on the type side.
/// </remarks>
internal abstract class MemberConstraint : Constraint
{
    private protected MemberConstraint(MemberSelection subject)
        : base(Guard.NotNull(subject, nameof(subject)).Source)
    {
        MemberSubject = subject;
    }

    internal MemberSelection MemberSubject { get; }
}
