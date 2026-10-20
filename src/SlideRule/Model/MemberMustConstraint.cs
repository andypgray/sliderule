namespace SlideRule.Model;

/// <summary>
///     The member constraint-position escape hatch (<c>.Methods.Must(pred, description:)</c>, GRAMMAR
///     §5.6, §5.7), one of the <see cref="PredicateNodes" />: its bare-infinitive description completes
///     "must …".
/// </summary>
internal sealed class MemberMustConstraint(MemberSelection subject, Func<IMemberInfo, bool> predicate, string description)
    : MemberConstraint(subject)
{
    internal Func<IMemberInfo, bool> Predicate { get; } = predicate;

    internal string Description { get; } = description;

    internal override string VerbPhrase => "must " + Description;
}
