namespace SlideRule.Model;

/// <summary>
///     The constraint-position escape hatch (<c>.Must(pred, description:)</c>, GRAMMAR §5.6), one of the
///     <see cref="PredicateNodes" />: its bare-infinitive description completes "must …" ("must keep type
///     names at or under 40 characters").
/// </summary>
internal sealed class MustConstraint(Selection subject, Func<ITypeInfo, bool> predicate, string description)
    : Constraint(subject)
{
    internal Func<ITypeInfo, bool> Predicate { get; } = predicate;

    internal string Description { get; } = description;

    internal override string VerbPhrase => "must " + Description;
}
