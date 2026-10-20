namespace SlideRule.Model;

/// <summary>
///     The project constraint-position escape hatch (<c>.Must(pred, description:)</c>, GRAMMAR §5.6,
///     §4.10), one of the <see cref="PredicateNodes" />: its bare-infinitive description completes "must …"
///     ("must declare a package description").
/// </summary>
/// <remarks>
///     Named with the <c>Project</c> prefix where the packaging verbs' nodes are bare, because
///     <see cref="MustConstraint" /> is the type-side escape hatch and two nodes cannot share a name. The
///     verb the author writes is <c>Must</c> on either stratum, and the self-spec's verb ledger strips the
///     prefix exactly as it strips <c>Member</c>.
/// </remarks>
internal sealed class ProjectMustConstraint(ProjectSelection subject, Func<IProjectInfo, bool> predicate, string description)
    : ProjectConstraint(subject)
{
    internal Func<IProjectInfo, bool> Predicate { get; } = predicate;

    internal string Description { get; } = description;

    internal override string VerbPhrase => "must " + Description;
}
