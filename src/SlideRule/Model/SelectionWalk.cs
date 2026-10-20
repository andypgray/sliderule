namespace SlideRule.Model;

/// <summary>
///     The canonical walk over every selection a constraint reaches — subject, operands, union parts and
///     <c>Except</c> payloads — held in one place so validation and rendering agree on what a rule
///     talks about.
/// </summary>
/// <remarks>
///     Enumeration order is part of the contract: validation reports its errors in walk order, so a
///     reordering here reorders diagnostics.
/// </remarks>
internal static class SelectionWalk
{
    /// <summary>The constraint's subject tree followed by each operand's tree, in operand order.</summary>
    /// <remarks>
    ///     A project constraint, whose subject is null, walks out empty here and through
    ///     <see cref="ProjectSelections" /> instead.
    /// </remarks>
    internal static IEnumerable<Selection> Selections(this Constraint constraint)
    {
        if (constraint.Subject is { } subject)
            foreach (Selection selection in ExpandSelection(subject))
                yield return selection;

        foreach (Selection operand in constraint.Operands)
        foreach (Selection selection in ExpandSelection(operand))
            yield return selection;

        // A member subject's own source is Constraint.Subject, walked above; each of its Except payloads draws
        // its members from a type selection of its own, which every walk over this one must reach too.
        if (constraint is MemberConstraint member)
            foreach (MemberSelection payload in member.MemberSubject.ExceptPayloads())
            foreach (Selection selection in ExpandSelection(payload.Source))
                yield return selection;
    }

    /// <summary>
    ///     Every member selection nested under <paramref name="selection" />'s <c>Except</c> adjectives, in
    ///     authoring order, each followed by the payloads it nests in turn — the selection itself excluded.
    /// </summary>
    internal static IEnumerable<MemberSelection> ExceptPayloads(this MemberSelection selection)
    {
        foreach (MemberAdjective adjective in selection.Adjectives)
        {
            if (adjective is not MemberExceptAdjective except) continue;

            foreach (MemberSelection payload in except.Payloads)
            {
                yield return payload;

                foreach (MemberSelection nested in payload.ExceptPayloads()) yield return nested;
            }
        }
    }

    /// <summary>
    ///     The project selections a constraint reaches: its project subject and the payloads nested under that
    ///     subject's <c>Except</c> adjectives, the project form of a family subject, or nothing. The
    ///     project-stratum twin of <see cref="Selections" />.
    /// </summary>
    internal static IEnumerable<ProjectSelection> ProjectSelections(this Constraint constraint)
    {
        if (constraint is ProjectConstraint project)
        {
            foreach (ProjectSelection selection in ExpandProjectSelection(project.ProjectSubject)) yield return selection;

            yield break;
        }

        // The one project selection a type-subject constraint can carry: a project family's (GRAMMAR §5.1). It
        // rides this walk so the project-side checks (§8 items 22–23) reach it without a walk of their own, so a
        // reader here may not assume a project constraint and reads the anchor it was given.
        if (constraint.Subject.FamilyNounOrNull()?.Projects is { } projects)
            foreach (ProjectSelection selection in ExpandProjectSelection(projects))
                yield return selection;
    }

    /// <summary>
    ///     The member selections a constraint reaches: its member subject, then every payload nested under that
    ///     subject's <c>Except</c> adjectives in <see cref="ExceptPayloads" /> order, or nothing at all for a
    ///     type- or project-subject constraint. The member-stratum twin of <see cref="ProjectSelections" />.
    /// </summary>
    internal static IEnumerable<MemberSelection> MemberSelections(this Constraint constraint)
    {
        if (constraint is not MemberConstraint member) yield break;

        yield return member.MemberSubject;

        foreach (MemberSelection payload in member.MemberSubject.ExceptPayloads()) yield return payload;
    }

    /// <summary>
    ///     The noun head of a selection, or null for a <see cref="UnionSelection" />, whose
    ///     <see cref="Selection.Noun" /> throws (GRAMMAR §5.1), and for a null receiver.
    /// </summary>
    internal static SelectionNoun? NounOrNull(this Selection? selection)
    {
        return selection is null or UnionSelection ? null : selection.Noun;
    }

    /// <summary>
    ///     The family noun of a family subject, or null for every other selection and for a null receiver
    ///     (GRAMMAR §5.1).
    /// </summary>
    internal static EachNoun? FamilyNounOrNull(this Selection? selection)
    {
        return selection.NounOrNull() as EachNoun;
    }

    /// <summary>The project selection itself, then the payloads of its own <c>Except</c> adjectives.</summary>
    /// <remarks>No union arm: the project stratum has no <c>AnyOf</c>, and <c>Except</c> is its only nesting.</remarks>
    private static IEnumerable<ProjectSelection> ExpandProjectSelection(ProjectSelection selection)
    {
        yield return selection;

        foreach (ProjectAdjective adjective in selection.Adjectives)
            if (adjective is ProjectExceptAdjective except)
                foreach (ProjectSelection nested in ExpandProjectSelection(except.Payload))
                    yield return nested;
    }

    /// <summary>
    ///     The selection itself, then its union parts or its family's layer cells, then the payloads of
    ///     its own <c>Except</c> adjectives.
    /// </summary>
    internal static IEnumerable<Selection> ExpandSelection(Selection selection)
    {
        yield return selection;

        // As in SpecValidator.SelectionProse: a union carries adjectives of its own, so AnyOf(a, b).Except(bad)
        // must reach the payload walk too, and a family's layer cells are walked on the same terms, so a cell
        // minted on another Arch is reached (GRAMMAR §5.1, §8). The arms are exclusive because reading .Noun on a
        // union throws.
        if (selection is UnionSelection union)
            foreach (Selection member in union.Parts)
            foreach (Selection nested in ExpandSelection(member))
                yield return nested;
        else if (selection.Noun is EachNoun { Layers: { } cells })
            foreach (Layer cell in cells)
            foreach (Selection nested in ExpandSelection(cell))
                yield return nested;

        foreach (SelectionAdjective adjective in selection.Adjectives)
            if (adjective is ExceptAdjective except)
                foreach (Selection nested in ExpandSelection(except.Payload))
                    yield return nested;
    }
}
