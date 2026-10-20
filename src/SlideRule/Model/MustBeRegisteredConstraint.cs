namespace SlideRule.Model;

/// <summary>
///     <c>.MustBeRegistered()</c> → "must be registered" (GRAMMAR §5.3, §4.7). Membership is
///     <see cref="RegisteredNoun" />'s at any lifetime, so the verb and <c>arch.Registered()</c> cannot
///     drift.
/// </summary>
internal sealed class MustBeRegisteredConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => "must be registered";
}
