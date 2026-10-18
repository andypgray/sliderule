using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>
///     Cuts a rule's subject down to the parts a run over one project's compilation
///     (<see cref="NarrowingKind.OneProject" />) can see, before the rule is evaluated.
/// </summary>
/// <remarks>
///     <para>
///         Pure: it mints new nodes and writes to none of the ones it is given, so a model parsed once and
///         shared by every compilation of a long-lived compiler process is the same model after a cut as
///         before it. Adjectives ride onto the rebuilt node, because they narrow the union or the family
///         rather than its parts (GRAMMAR §5.1).
///     </para>
///     <para>
///         A part is empty on exactly the test <c>ConstraintEvaluator</c>'s own subject gate applies — one
///         collection in <see cref="SelectionPosition.Subject" /> position, empty at zero members — and the
///         two must agree: a part kept here that the evaluator then calls empty is reported as an authoring
///         error against the one universe that cannot judge it. The gate also fails each name of a
///         <c>Named</c> list that matched nothing, so the same agreement drops those names from the lists of
///         the subject and of each union part kept: a name another project declares is not this one's to judge.
///     </para>
///     <para>
///         A union's top-level parts are kept or dropped whole, a nested union being one such part: opening
///         it would change which parts the evaluator's per-operand gate sees and would lose the adjectives
///         the inner union carries. Where nothing is dropped, and where nothing survives, the subject comes
///         back as the very instance it arrived as — so a subject nothing can see reaches the evaluator's
///         empty-subject verdict exactly as an unnarrowed run's would, which is what the checker turns into
///         a skip.
///     </para>
/// </remarks>
internal static class OneProjectSubject
{
    /// <summary>
    ///     <paramref name="subject" /> without its empty parts: a union without the top-level parts that
    ///     matched nothing, a family of layers without the cells that did, and every other subject — a
    ///     family of projects included — as it stands. Where the subject matched something, its own
    ///     <c>Named</c> lists and those of the union parts kept also lose the names that matched nothing.
    /// </summary>
    /// <param name="selections">The run's selection evaluator, which resolves each part.</param>
    /// <param name="subject">The rule's own subject, which is left exactly as it was.</param>
    /// <returns>The subject to evaluate, or <paramref name="subject" /> itself where nothing was dropped.</returns>
    /// <exception cref="RuleEvaluationException">
    ///     A part could not be resolved at all — an unrepresentable anchor, a pattern the model refuses.
    /// </exception>
    internal static Selection Cut(SelectionEvaluator selections, Selection subject)
    {
        // The union arm first: UnionSelection.Noun throws by design, so reading the noun would fault here.
        Selection cut = subject switch
        {
            UnionSelection union => CutUnion(selections, union),
            { Noun: EachNoun { Layers: { } layers } } => CutFamily(selections, subject, layers),
            _ => subject
        };

        // Guarded, because collecting the whole subject a second time is paid by every rule in every build.
        return DeadNames.Possible(cut) ? Live(selections, cut) ?? cut : cut;
    }

    private static Selection CutUnion(SelectionEvaluator selections, UnionSelection union)
    {
        var kept = new List<Selection>(union.Parts.Count);
        var changed = false;
        foreach (Selection part in union.Parts)
        {
            Selection? live = Live(selections, part);
            changed |= !ReferenceEquals(live, part);
            if (live is not null) kept.Add(live);
        }

        if (!changed || kept.Count == 0) return union;

        // The internal constructor rather than UnionSelection.Create, which re-flattens a nested union.
        return new UnionSelection(union.Owner, kept).WithAdjectives(union.Adjectives);
    }

    // A selection as the evaluator's subject gate will read it here: null where it matched nothing, and
    // otherwise without the names of its own lists that matched nothing, which the gate would fail one by
    // one. The membership is unchanged by the drop, so the evaluator collects what was collected here.
    private static Selection? Live(SelectionEvaluator selections, Selection selection)
    {
        SelectionAdmission matched = SelectionAdmission.Collect(selections, selection, SelectionPosition.Subject);
        if (matched.Count == 0) return null;

        return DeadNames.Dropped(selection, matched.Nodes, selections);
    }

    private static Selection CutFamily(
        SelectionEvaluator selections, Selection subject, IReadOnlyList<Layer> layers)
    {
        List<Layer> kept = layers
            .Where(layer => !IsEmpty(selections, layer))
            .ToList();
        if (kept.Count == layers.Count || kept.Count == 0) return subject;

        return new RefinedSelection(subject.Owner, new EachNoun(kept), subject.Adjectives);
    }

    private static bool IsEmpty(SelectionEvaluator selections, Selection part)
    {
        SelectionAdmission matched = SelectionAdmission.Collect(selections, part, SelectionPosition.Subject);
        return matched.Count == 0;
    }
}
