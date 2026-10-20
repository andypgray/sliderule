namespace SlideRule.Model;

/// <summary>
///     The selector-position escape hatch (<c>.Where(pred, description:)</c>, GRAMMAR §5.6), one of the
///     <see cref="PredicateNodes" />: its description continues the noun phrase ("… whose name contains a
///     digit").
/// </summary>
internal sealed class WhereAdjective(Func<ITypeInfo, bool> predicate, string description) : SelectionAdjective
{
    internal Func<ITypeInfo, bool> Predicate { get; } = predicate;

    internal string Description { get; } = description;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override string Fragment => " " + Description;
}
