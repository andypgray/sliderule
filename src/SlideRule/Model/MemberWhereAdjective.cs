namespace SlideRule.Model;

/// <summary>
///     The member selector-position escape hatch (<c>.Where(pred, description:)</c> on a member
///     selection, GRAMMAR §5.6, §5.7), one of the <see cref="PredicateNodes" />: its description is a
///     relative clause on the member kind-plural.
/// </summary>
/// <remarks>
///     The clause lands after any <see cref="ReturningAdjective" /> and ahead of " declared in" and the
///     type reference, so it never reads as the type-side <see cref="WhereAdjective" /> (GRAMMAR §6). Its
///     placement stays <see cref="AdjectivePlacement.SubjectFinal" /> because that is what renders it after
///     every <see cref="ReturningAdjective" />, whatever the chain order. Member Excepts follow it because
///     member-subject assembly renders them after the type reference.
/// </remarks>
internal sealed class MemberWhereAdjective(Func<IMemberInfo, bool> predicate, string description) : MemberAdjective
{
    internal Func<IMemberInfo, bool> Predicate { get; } = predicate;

    internal string Description { get; } = description;

    internal override AdjectivePlacement Placement => AdjectivePlacement.SubjectFinal;

    internal override string Fragment => " " + Description;
}
