namespace SlideRule.Model;

/// <summary>
///     The project selector-position escape hatch (<c>.Where(pred, description:)</c>, GRAMMAR §5.6, §4.10),
///     one of the <see cref="PredicateNodes" />: its description continues the noun phrase ("… whose name ends
///     in a digit").
/// </summary>
internal sealed class ProjectWhereAdjective(Func<IProjectInfo, bool> predicate, string description) : ProjectAdjective
{
    internal Func<IProjectInfo, bool> Predicate { get; } = predicate;

    internal string Description { get; } = description;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override string Fragment => " " + Description;
}
