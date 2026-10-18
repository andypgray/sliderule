using SlideRule.Hosting;
using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>
///     Whether a rule's verdict follows from one project's compilation or takes the whole solution — a
///     pure function of the model, which reads no codebase.
/// </summary>
/// <remarks>
///     <para>
///         A rule is decidable from one project only when its verdict for a subject is a function of that
///         subject's own facts and of references sited in that project, with every selection it reads
///         evaluated pointwise. The answer errs toward the whole solution: a wrong "decidable" would let a
///         reader that sees one project's compilation report a rule that does not hold, where a wrong
///         "whole solution" costs only earliness.
///     </para>
///     <para>
///         The rows are per node class, and a rule's verdict is the first "out" the walk meets — one node
///         that needs the solution is enough, so the rule verdict is the minimum over what it reads.
///         Keeping each row honest about its own node rather than about the rules it can appear in is why
///         the project adjectives are in while no project rule is decidable: every verb that can take a
///         project subject is itself out.
///     </para>
///     <para>
///         Where a node stands is three facts, not one, and each row keys on the ones its own argument
///         rests on. <see cref="OneProjectPosition" /> holds the three and says what each asks, and a row
///         that reads an axis says why beside the row. Three out answers were
///         each settled by comparing a one-project verdict with the whole solution's over the same code:
///         the project noun where it exempts by plain membership (an <c>Except</c> payload subtracting
///         from a ban), <see cref="MustResideInProjectConstraint" /> and
///         <see cref="MustBelongToConstraint" />. The family noun is held out by its shape alone, since no
///         comparison has been run over it.
///     </para>
///     <para>
///         The verdict is derived, never stored: writing it into a model file would be a second answer that
///         could disagree with the rows.
///     </para>
/// </remarks>
internal static class OneProjectDecidability
{
    /// <summary>A predicate the spec wrote in code, which only a run of that code answers.</summary>
    private const string CodeReason = "Holds a predicate written in code.";

    /// <summary>Container registrations, which are gathered from wherever in the solution they are written.</summary>
    private const string RegisteredReason =
        "Reads dependency-injection registrations, which are collected across the whole solution.";

    /// <summary>Generated-code filtering applied to types other projects declare.</summary>
    private const string AuthoredReason =
        "Filters generated code out of a selection of other types, and one project cannot tell whether a "
        + "type another project declares is generated.";

    /// <summary>
    ///     An attribute filter applied to types another project declares. Side-keyed and not
    ///     polarity-keyed, because the error was measured running both ways at once over the same sibling:
    ///     an image is missing the attribute the sibling's own build conditioned away, and carries one the
    ///     compiler wrote onto the type as it emitted it.
    /// </summary>
    private const string AttributedWithReason =
        "Selects other types by attribute, and a project compiled against another's assembly does not see "
        + "every attribute its source declares.";

    /// <summary>
    ///     A path glob applied to types another project declares: a project compiled against another's assembly
    ///     sees those types without the source files they were declared in, so it has no path to read.
    /// </summary>
    private const string UnderReason =
        "Selects other types by the files declaring them, and a project compiled against another's assembly does "
        + "not see its source files.";

    /// <summary>
    ///     A project noun standing where plain membership spares a type, which asks who declares it — and
    ///     answers for every copy at once, so no edge's attribution narrows the question back down.
    /// </summary>
    private const string DeclarerReason =
        "Spares a type by any project that declares it, and one project's compilation cannot tell when "
        + "another project compiles the same type, so it would report what the whole solution exempts.";

    /// <summary>A family noun, whose cells are partitioned and compared across the solution.</summary>
    private const string FamilyReason = "Ranges over a family, which is judged across the whole solution.";

    /// <summary>A verb that judges the cells of a family against one another.</summary>
    private const string AmongReason =
        "Judges references among a family of layers, which takes the whole solution.";

    /// <summary>A verb whose subject is a cycle, which no single project can see the whole of.</summary>
    private const string CircleReason =
        "A circle of references can pass through any project, so finding one takes the whole solution.";

    /// <summary>A verb that counts types carrying a derived name, wherever they are declared.</summary>
    private const string CounterpartReason =
        "A counterpart may be declared in any project, so the rule takes the whole solution.";

    /// <summary>A node whose facts come from a project file rather than from a compilation.</summary>
    /// <remarks>
    ///     The declared project references are among those facts. The list a one-project view can build is
    ///     the compiler's referenced closure instead, so a project verb that reads references can become
    ///     decidable only once the build tier is handed the declared list.
    /// </remarks>
    private const string ProjectReason = "Reads project-file facts, and one project's compilation carries none.";

    /// <summary>The residency verb, whose answer is the list of projects that declare a type.</summary>
    private const string ResidencyReason =
        "A type compiled into several projects resides in each of them, so where a type resides takes the "
        + "whole solution.";

    /// <summary>
    ///     The coverage verb, held out whole: a cell a project defines holds a type twice over when two
    ///     projects compile it, and the row does not tell that spelling from a cell defined by namespace.
    /// </summary>
    private const string MembershipReason =
        "Which layer a type belongs to can depend on every project that compiles it, so coverage is left to "
        + "the whole solution.";

    /// <summary>A rule with no constraint, which judges a diff rather than the code.</summary>
    private const string TripwireReason =
        "A tripwire compares against a diff, which one project's compilation does not carry.";

    private static readonly Dictionary<Type, Func<OneProjectPosition, string?>> Rows = BuildRows();

    /// <summary>Every node class the table classifies — the registry's keys, for the inventory sweep.</summary>
    internal static IReadOnlyCollection<Type> ClassifiedTypes => Rows.Keys;

    /// <summary>
    ///     Whether the row for <paramref name="node" /> is decidable — the registry's values, for the
    ///     inventory sweep that holds every decidable verb to a diagnostic a build really reported. Read at
    ///     <see cref="OneProjectPosition.Verb" />, which is the only reading a constraint class has: every
    ///     constraint row is position-blind, and the sweep asks about constraint classes alone. False for a
    ///     node class with no row at all.
    /// </summary>
    internal static bool Decides(Type node)
    {
        return Rows.TryGetValue(node, out Func<OneProjectPosition, string?> row)
               && row(OneProjectPosition.Verb) is null;
    }

    /// <summary>The verdict for one rule: decidable, or the reason of the first node that says otherwise.</summary>
    internal static OneProjectVerdict Classify(ArchRule rule)
    {
        return new OneProjectVerdict(ReasonAgainst(rule));
    }

    // The fixed order: a rule with no constraint, then the verb, then the subject, then the operands, then
    // whichever second subject the verb carries. The order is what makes the reason reproducible — several
    // nodes of one rule can each be out, and the reader is told about the one nearest the verb.
    private static string? ReasonAgainst(ArchRule rule)
    {
        if (rule.Constraint is not { } constraint) return TripwireReason;

        if (Row(constraint, OneProjectPosition.Verb) is { } verb) return verb;

        OneProjectPosition subjectPosition = SubjectPositionOf(constraint);
        if (constraint.Subject is { } subject
            && WalkSelection(subject, subjectPosition) is { } subjectReason)
            return subjectReason;

        var operandPosition = new OneProjectPosition(
            SelectionPosition.Target, OperandPolarityOf(constraint), ReadingOf(constraint));
        foreach (Selection operand in constraint.Operands)
            if (WalkSelection(operand, operandPosition) is { } operandReason)
                return operandReason;

        if (constraint is MemberConstraint member) return WalkMemberSubject(member.MemberSubject, subjectPosition);

        if (constraint is ProjectConstraint project)
            return WalkProjectSelection(project.ProjectSubject, OneProjectPosition.Conservative);

        return null;
    }

    // Where a verb's own subject stands, on all three axes. The SIDE is the concession that a wrong verdict
    // about another project's type is reported at that type and so never reaches this reader — and that is
    // false of the three inbound verbs, whose violations are sited on the referencing side and so land in the
    // code this reader does see. Containment desugars to MustOnlyBeReferencedBy and needs no row of its own.
    // A reference allow-list holds its own subject, so that subject exempts as much as it bounds; a project
    // noun there stays decidable all the same, because the verb asks it of one edge instance.
    private static OneProjectPosition SubjectPositionOf(Constraint constraint)
    {
        bool sitedAtTheReferrer = ReferenceVerbs.SubjectAtTarget(constraint);
        bool allowsItself = ReferenceVerbs.AllowsSubject(constraint);

        return new OneProjectPosition(
            sitedAtTheReferrer ? SelectionPosition.Target : SelectionPosition.Subject,
            allowsItself ? OneProjectPolarity.Exempting : OneProjectPolarity.Banning,
            ReadingOf(constraint));
    }

    // What a verb's selections are asked of, subject and operands alike: a verb either walks edges or it
    // does not, and both its ends are read the same way. The sixteen edge verbs are every OperandConstraint
    // but the two that carry no edge, plus the two leaf allow-lists and the two family laws — each of them
    // reaches SelectionAdmission.CountedSites, which asks its two admissions at the project each instance
    // named (GRAMMAR §4.1). Everything else asks SelectionAdmission.Contains, of the type and so of every
    // copy of it at once.
    //
    // It answers rather than throws, for OperandPolarityOf's reason and with the same default: a verb that
    // ships without an arm here reads as plain membership, which is the OUT direction for the one row that
    // reads this axis.
    private static OneProjectReading ReadingOf(Constraint constraint)
    {
        bool walksEdges =
            constraint is OperandConstraint
                and not (MustBelongToConstraint or MustHaveExactlyOneCounterpartConstraint)
            || constraint is MustOnlyReferenceItselfConstraint
                or MustOnlyBeReferencedByItselfConstraint
                or MustNotReferenceEachOtherConstraint
                or MustNotHaveCircularReferencesConstraint;

        return walksEdges ? OneProjectReading.EdgeEnd : OneProjectReading.Membership;
    }

    // What a verb's operands do to a verdict. The nine banning verbs are named positively and everything
    // else falls through to Exempting, each row derived from the evaluator's own arm rather than from the
    // verb's name: every verb below reaches ConstraintEvaluator.ForbiddenEdge and asks
    // SelectionAdmission.CountedSites with `wantHit: true`, where the allow-list pair asks the same test
    // with `wantHit: false`. Exactly fourteen classes derive from OperandConstraint, so the table is
    // closed; the five it does not name are MustOnlyReference and MustOnlyBeReferencedBy, MustOnlyThrow,
    // MustBelongTo (a subject no membership holds is red) and MustHaveExactlyOneCounterpart (a missed
    // `Among` entry drops the count to nought), the last two being out by their own verb rows already.
    //
    // It answers rather than throws, and the asymmetry with Row is deliberate: Row may throw only because
    // ModelNodeInventoryTests holds every node class to a row and makes the throw unreachable, and nothing
    // gates this table the same way, while the analyzer's model load promises never to throw. A verb that
    // ships without an arm here therefore defaults to the OUT direction — it costs earliness, where the
    // other default would cost the promise the build tier rests on. Do not make it consistent with Row.
    private static OneProjectPolarity OperandPolarityOf(Constraint constraint)
    {
        bool bans = constraint is MustNotReferenceConstraint
            or MustNotBeReferencedByConstraint
            or MustNotConstructConstraint
            or MustNotInjectConstraint
            or MustNotExposeConstraint
            or MustNotCatchConstraint
            or MustNotCatchUnfilteredConstraint
            or MustNotSwallowConstraint
            or MustNotThrowConstraint;

        return bans ? OneProjectPolarity.Banning : OneProjectPolarity.Exempting;
    }

    // The member subject's own source is not walked here: it is the constraint's own subject, handed up as
    // MemberConstraint.Subject, which ReasonAgainst has already walked. An Except payload's source is the one
    // type selection a member selection nests, so it is walked here, at the position an exclusion stands in
    // (AsExceptPayload) — a project noun there spares by plain membership and is out on DeclarerReason, as on
    // the type side. No member node holds a position-sensitive row, so the member selections and their
    // adjectives answer the same at whatever position they are read.
    private static string? WalkMemberSubject(MemberSelection subject, OneProjectPosition position)
    {
        if (Row(subject, position) is { } shape) return shape;

        foreach (MemberAdjective adjective in subject.Adjectives)
        {
            if (Row(adjective, position) is { } reason) return reason;

            if (adjective is not MemberExceptAdjective except) continue;

            OneProjectPosition payloadPosition = position.AsExceptPayload();
            foreach (MemberSelection payload in except.Payloads)
            {
                if (WalkSelection(payload.Source, payloadPosition) is { } sourceReason) return sourceReason;
                if (WalkMemberSubject(payload, payloadPosition) is { } payloadReason) return payloadReason;
            }
        }

        return null;
    }

    private static string? WalkSelection(Selection selection, OneProjectPosition position)
    {
        if (Row(selection, position) is { } shape) return shape;

        // A union has no single noun; its parts stand where the union stands, so they keep its position.
        if (selection is UnionSelection union)
        {
            foreach (Selection part in union.Parts)
                if (WalkSelection(part, position) is { } partReason)
                    return partReason;
        }
        else if (WalkNoun(selection.Noun, position) is { } nounReason)
        {
            return nounReason;
        }

        foreach (SelectionAdjective adjective in selection.Adjectives)
        {
            if (Row(adjective, position) is { } reason) return reason;

            // An exclusion stands at its own position rather than at the one it was written inside: it is
            // read over the whole solution even within a subject, since a type this project cannot see can
            // still be one of the types that are NOT the subject, and it spares where its host bans.
            if (adjective is ExceptAdjective except
                && WalkSelection(except.Payload, position.AsExceptPayload()) is { } payloadReason)
                return payloadReason;
        }

        return null;
    }

    // What a noun holds is walked whatever the noun's own row says, so the walk stays a walk of the model
    // rather than a second copy of the table.
    private static string? WalkNoun(SelectionNoun noun, OneProjectPosition position)
    {
        if (Row(noun, position) is { } reason) return reason;

        // A layer is transparent to its definition, and a reference to it reaches that definition exactly
        // where the reference stands, so the definition is read at the layer's own position.
        if (noun is LayerNoun { Definition: { } definition })
            return WalkSelection(definition, position);

        if (noun is not EachNoun family) return null;

        if (family.Layers is { } cells)
        {
            foreach (Layer cell in cells)
                if (WalkSelection(cell, OneProjectPosition.Conservative) is { } cellReason)
                    return cellReason;

            return null;
        }

        return WalkProjectSelection(family.Projects!, OneProjectPosition.Conservative);
    }

    // Both arms that reach here are unreachable in a built model — every ProjectConstraint subclass has an
    // unconditional out row, and the family noun's own row answers before its cells are walked — so the
    // six project-side rows exist to satisfy the node inventory and the position this walks at is not a
    // live axis. Read nothing into it.
    private static string? WalkProjectSelection(ProjectSelection selection, OneProjectPosition position)
    {
        if (Row(selection, position) is { } shape) return shape;

        foreach (ProjectAdjective adjective in selection.Adjectives)
        {
            if (Row(adjective, position) is { } reason) return reason;

            if (adjective is ProjectExceptAdjective except
                && WalkProjectSelection(except.Payload, position.AsExceptPayload()) is { } payloadReason)
                return payloadReason;
        }

        return null;
    }

    // Fail closed, like the evaluator's own dispatch: a node class nobody classified is a node whose cost
    // to a one-project reader nobody has thought about, and answering "decidable" for it is the dangerous
    // direction.
    private static string? Row(object node, OneProjectPosition position)
    {
        Type type = node.GetType();
        if (!Rows.TryGetValue(type, out Func<OneProjectPosition, string?> row))
            throw new InvalidOperationException($"Unclassified model node '{type.Name}'.");

        return row(position);
    }

    private static Dictionary<Type, Func<OneProjectPosition, string?>> BuildRows()
    {
        var rows = new Dictionary<Type, Func<OneProjectPosition, string?>>();
        AddNouns(rows);
        AddAdjectives(rows);
        AddMemberAdjectives(rows);
        AddProjectAdjectives(rows);
        AddTypeVerbs(rows);
        AddMemberVerbs(rows);
        AddProjectVerbs(rows);
        AddSelections(rows);
        Out(rows, CodeReason, [.. PredicateNodes.Types]);
        return rows;
    }

    private static void AddNouns(Dictionary<Type, Func<OneProjectPosition, string?>> rows)
    {
        In(rows,
            typeof(TypesNoun),
            typeof(TypeNoun),
            typeof(NamespaceNoun),
            typeof(LayerNoun));

        // The one row reading two axes, and neither of them the side: one compilation's membership is a
        // subset of the whole solution's, so a project noun that bans can only miss a type and report less.
        // One that exempts spares fewer types and reports more — unless the question is narrowed to one
        // edge instance, where the project asked about is the one that instance reached and a compilation
        // attributes its own instances exactly. So only plain membership that exempts is out.
        rows.Add(
            typeof(ProjectNoun),
            position => position.Polarity == OneProjectPolarity.Exempting
                        && position.Reading == OneProjectReading.Membership
                ? DeclarerReason
                : null);

        Out(rows, RegisteredReason, typeof(RegisteredNoun));
        Out(rows, FamilyReason, typeof(EachNoun));
    }

    private static void AddAdjectives(Dictionary<Type, Func<OneProjectPosition, string?>> rows)
    {
        In(rows,
            typeof(InNamespaceAdjective),
            typeof(OfKindAdjective),
            typeof(AccessibilityAdjective),
            typeof(WithSuffixAdjective),
            typeof(WithPrefixAdjective),
            typeof(WithNameMatchingAdjective),
            typeof(NamedAdjective),
            typeof(ImplementingAdjective),
            typeof(DerivedFromAdjective),
            typeof(ExceptAdjective));

        // The side-keyed adjective rows: what each costs depends on whose types it is filtering. Neither of
        // these two is rescued by polarity — a filter that can answer either way over another project's types
        // both admits types the whole solution excludes and excludes ones it admits.
        rows.Add(
            typeof(AuthoredAdjective),
            position => position.Side == SelectionPosition.Target ? AuthoredReason : null);
        rows.Add(
            typeof(AttributedWithAdjective),
            position => position.Side == SelectionPosition.Target ? AttributedWithReason : null);

        // The third side-keyed row. The own project's types carry the paths of the files that declare them, so a
        // glob over them is decidable; another project's carry none here, so a glob can only exclude them. A ban
        // would survive that, but an allow-list and an Except payload, which stand on the same side, would not, and
        // the row keys on the side rather than on which of the three a position is.
        rows.Add(
            typeof(UnderAdjective),
            position => position.Side == SelectionPosition.Target ? UnderReason : null);
    }

    private static void AddMemberAdjectives(Dictionary<Type, Func<OneProjectPosition, string?>> rows)
    {
        In(rows,
            typeof(MemberWithSuffixAdjective),
            typeof(MemberWithPrefixAdjective),
            typeof(MemberWithNameMatchingAdjective),
            typeof(MemberNamedAdjective),
            typeof(ReturningAdjective),
            typeof(MemberAttributedWithAdjective),
            typeof(MemberThatAreStaticAdjective),
            typeof(MemberAccessibilityAdjective),
            typeof(MemberExceptAdjective));
    }

    private static void AddProjectAdjectives(Dictionary<Type, Func<OneProjectPosition, string?>> rows)
    {
        In(rows,
            typeof(ProjectNamedAdjective),
            typeof(ProjectMatchingAdjective),
            typeof(ProjectExceptAdjective));

        Out(rows, ProjectReason, typeof(ProjectPackableAdjective));
    }

    private static void AddTypeVerbs(Dictionary<Type, Func<OneProjectPosition, string?>> rows)
    {
        In(rows,
            typeof(MustBeSealedConstraint),
            typeof(MustBeStaticConstraint),
            typeof(MustBeAbstractConstraint),
            typeof(MustBePublicConstraint),
            typeof(MustBeInternalConstraint),
            typeof(MustOnlyReferenceItselfConstraint),
            typeof(MustOnlyBeReferencedByItselfConstraint),
            typeof(MustNotReferenceConstraint),
            typeof(MustNotBeReferencedByConstraint),
            typeof(MustOnlyReferenceConstraint),
            typeof(MustOnlyBeReferencedByConstraint),
            typeof(MustNotConstructConstraint),
            typeof(MustNotInjectConstraint),
            typeof(MustNotCatchConstraint),
            typeof(MustNotCatchUnfilteredConstraint),
            typeof(MustNotSwallowConstraint),
            typeof(MustNotThrowConstraint),
            typeof(MustOnlyThrowConstraint),
            typeof(MustNotExposeConstraint),
            typeof(MustImplementConstraint),
            typeof(MustDeriveFromConstraint),
            typeof(MustBeAttributedWithConstraint),
            typeof(MustNotImplementConstraint),
            typeof(MustNotDeriveFromConstraint),
            typeof(MustNotBeAttributedWithConstraint),
            typeof(MustResideInNamespaceConstraint),
            typeof(MustHaveSuffixConstraint),
            typeof(MustHavePrefixConstraint),
            typeof(MustHaveNameMatchingConstraint),
            typeof(MustNotUseConstraint));

        Out(rows, RegisteredReason, typeof(MustBeRegisteredConstraint));
        Out(rows, AmongReason, typeof(MustNotReferenceEachOtherConstraint));
        Out(rows, CircleReason, typeof(MustNotHaveCircularReferencesConstraint));
        Out(rows, CounterpartReason, typeof(MustHaveExactlyOneCounterpartConstraint));
        Out(rows, ResidencyReason, typeof(MustResideInProjectConstraint));
        Out(rows, MembershipReason, typeof(MustBelongToConstraint));
    }

    private static void AddMemberVerbs(Dictionary<Type, Func<OneProjectPosition, string?>> rows)
    {
        In(rows,
            typeof(MemberMustBePublicConstraint),
            typeof(MemberMustBeInternalConstraint),
            typeof(MemberMustBeProtectedConstraint),
            typeof(MemberMustBePrivateConstraint),
            typeof(MemberMustBeStaticConstraint),
            typeof(MemberMustBeAbstractConstraint),
            typeof(MemberMustBeVirtualConstraint),
            typeof(MemberMustBeOverridableConstraint),
            typeof(MemberMustBeGetOnlyConstraint),
            typeof(MemberMustBeReadonlyConstraint),
            typeof(MemberMustHaveSuffixConstraint),
            typeof(MemberMustHavePrefixConstraint),
            typeof(MemberMustHaveNameMatchingConstraint),
            typeof(MemberMustBeAttributedWithConstraint),
            typeof(MemberMustNotBeAttributedWithConstraint),
            typeof(MemberMustAcceptParameterConstraint));
    }

    private static void AddProjectVerbs(Dictionary<Type, Func<OneProjectPosition, string?>> rows)
    {
        Out(rows, ProjectReason,
            typeof(MustReferenceNoPackagesConstraint),
            typeof(MustLockPackagesConstraint),
            typeof(MustNotBePackableConstraint),
            typeof(MustOnlyTargetConstraint),
            typeof(MustTargetConstraint),
            typeof(MustOnlyReferencePackagesConstraint),
            typeof(MustNotReferencePackagesConstraint),
            typeof(MustSetPropertyConstraint));
    }

    private static void AddSelections(Dictionary<Type, Func<OneProjectPosition, string?>> rows)
    {
        In(rows,
            typeof(RefinedSelection),
            typeof(UnionSelection),
            typeof(Layer),
            typeof(KindMemberSelection),
            typeof(MethodSelection),
            typeof(PropertySelection),
            typeof(FieldSelection),
            typeof(RefinedProjectSelection));
    }

    private static void In(Dictionary<Type, Func<OneProjectPosition, string?>> rows, params Type[] nodes)
    {
        foreach (Type node in nodes) rows.Add(node, _ => null);
    }

    private static void Out(
        Dictionary<Type, Func<OneProjectPosition, string?>> rows, string reason, params Type[] nodes)
    {
        foreach (Type node in nodes) rows.Add(node, _ => reason);
    }
}
