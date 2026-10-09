using SlideRule.Codebase;
using SlideRule.Fluent;
using SlideRule.Model;
using SlideRule.Prose;

namespace SlideRule.Checking;

/// <summary>
///     How much of a rule's materialized subject set a generator emitted: <see cref="Types" /> is the whole
///     set, <see cref="Generated" /> the part of it no author wrote. A struct so that
///     <see langword="default" /> is the zero pair — what a rule that never reached a subject at all
///     (errored, skipped, empty) reports, with no null to thread through the checker.
/// </summary>
internal readonly struct SubjectCoverage
{
    public SubjectCoverage(int types, int generated)
    {
        Types = types;
        Generated = generated;
    }

    /// <summary>The size of the materialized subject set.</summary>
    public int Types { get; }

    /// <summary>How many of <see cref="Types" /> a generator emitted.</summary>
    public int Generated { get; }
}

/// <summary>
///     Evaluates one <see cref="Constraint" /> against the codebase, per-verb (GRAMMAR §4.1, §4.3,
///     §4.5, §4.7, §4.8, §4.9, §5.3): each dependency verb walks the <see cref="CodebaseModel" /> edge
///     list its family is extracted into, and each shape verb tests every subject directly.
/// </summary>
/// <remarks>
///     Every verb first requires a non-empty subject set — an empty subject fails the rule by default
///     (GRAMMAR §4.1). Violations come back unordered (the caller sorts), alongside any inert-target
///     warnings.
/// </remarks>
internal sealed class ConstraintEvaluator
{
    /// <summary>The pinned message on an empty-subject failure (ArchUnit precedent, GRAMMAR §4.1).</summary>
    internal const string EmptySubjectMessage = "The subject selection matched no solution-declared types.";

    /// <summary>The pinned message on an empty <em>member</em> subject failure (the member analog, GRAMMAR §4.6).</summary>
    internal const string EmptyMemberSubjectMessage = "The subject selection matched no solution-declared members.";

    /// <summary>The pinned message on an empty <em>project</em> subject failure (the project analog, GRAMMAR §4.10).</summary>
    internal const string EmptyProjectSubjectMessage = "The subject selection matched no solution-declared projects.";

    /// <summary>
    ///     The pinned warning text every forbidden-set verb raises on an inert target (GRAMMAR §4.1) —
    ///     one string for the whole family, so a correction to what "inert" means cannot land on some
    ///     verbs and miss others.
    /// </summary>
    private const string InertTargetMessage = "This rule is inert: its target selection matched no types.";

    private static readonly IReadOnlyList<CheckWarning> NoWarnings = Array.Empty<CheckWarning>();

    private readonly EdgeIndex<CatchEdge> _catchEdgesBySource;
    private readonly EdgeIndex<ConstructorEdge> _constructorEdgesBySource;
    private readonly EdgeIndex<ReferenceEdge> _edgesBySource;
    private readonly EdgeIndex<ReferenceEdge> _edgesByTarget;
    private readonly EdgeIndex<ExposureEdge> _exposureEdgesBySource;
    private readonly IncompleteModel? _incompleteModel;
    private readonly EdgeIndex<InjectionEdge> _injectionEdgesBySource;
    private readonly EdgeIndex<MemberEdge> _memberEdgesBySource;
    private readonly NarrowedUniverse? _narrowing;
    private readonly IReadOnlyList<ProjectNode> _projects;
    private readonly SelectionEvaluator _selections;
    private readonly EdgeIndex<ThrowEdge> _throwEdgesBySource;

    internal ConstraintEvaluator(
        CodebaseModel model, SelectionEvaluator selections, IncompleteModel? incompleteModel = null,
        NarrowedUniverse? narrowing = null)
    {
        _incompleteModel = incompleteModel;
        _narrowing = narrowing;
        _projects = model.Projects;
        _edgesBySource = new EdgeIndex<ReferenceEdge>(model.Edges, e => e.Source);
        _edgesByTarget = new EdgeIndex<ReferenceEdge>(model.Edges, e => e.Target);
        _memberEdgesBySource = new EdgeIndex<MemberEdge>(model.MemberEdges, e => e.Source);
        _constructorEdgesBySource = new EdgeIndex<ConstructorEdge>(model.ConstructorEdges, e => e.Source);
        _injectionEdgesBySource = new EdgeIndex<InjectionEdge>(model.InjectionEdges, e => e.Source);
        _catchEdgesBySource = new EdgeIndex<CatchEdge>(model.CatchEdges, e => e.Source);
        _throwEdgesBySource = new EdgeIndex<ThrowEdge>(model.ThrowEdges, e => e.Source);
        _exposureEdgesBySource = new EdgeIndex<ExposureEdge>(model.ExposureEdges, e => e.Source);
        _selections = selections;
    }

    /// <summary>
    ///     The pinned message on an empty union <em>operand</em> in subject position (GRAMMAR §4.1): a typo'd
    ///     project name inside a four-way union must not be masked by its siblings, so the operand that
    ///     matched nothing is named in its own right.
    /// </summary>
    internal static string EmptyOperandMessage(string operandReference)
    {
        return $"The subject selection operand \"{operandReference}\" matched no solution-declared types.";
    }

    /// <summary>
    ///     Evaluates <paramref name="constraint" /> over <paramref name="subject" />: the constraint's own
    ///     subject, or what a caller that narrowed it before evaluation hands in (GRAMMAR §4.1).
    /// </summary>
    /// <remarks>
    ///     Null exactly where <c>Constraint.Subject</c> is: a project constraint, which dispatches before the
    ///     subject is read at all. Every gate below judges what it is handed, so the evaluator's own reading
    ///     of an empty subject — an authoring error — is unchanged and belongs to whoever chose the subject.
    /// </remarks>
    internal (IReadOnlyList<Violation> Violations, IReadOnlyList<CheckWarning> Warnings, SubjectCoverage Coverage)
        Evaluate(Constraint constraint, Selection? subject)
    {
        // A project constraint ranges over the solution's PROJECTS (GRAMMAR §4.10), so it dispatches before
        // everything below: it has no type subject to resolve at all — Constraint.Subject is null for it —
        // and its own empty check speaks in project terms.
        if (constraint is ProjectConstraint projectConstraint) return EvaluateProject(projectConstraint);

        // Loud per-operand emptiness for a union subject (GRAMMAR §4.1), ahead of the member dispatch because
        // MemberConstraint.Subject IS the underlying type selection — so one gate covers the type- and
        // member-subject paths alike. The gate hands back the admission it folded the operands into, so
        // the union subject is neither evaluated a second time nor attributed a second time. The bang is the
        // project dispatch above: every constraint that reaches here carries a type selection.
        (IReadOnlyList<Violation> emptyOperands, SelectionAdmission? resolved, FamilySubject? family) =
            ResolveSubject(subject!);
        if (resolved is not { } admission) return (emptyOperands, NoWarnings, default);

        // A member-subject constraint (GRAMMAR §4.6) ranges over declared members, so it dispatches before
        // the type-subject gate: its own empty check speaks in member terms (a type subject that matches
        // types none of whose members survive the kind filter is the ordinary way to fail empty).
        if (constraint is MemberConstraint memberConstraint) return EvaluateMember(memberConstraint, admission.Nodes);

        HashSet<TypeNode> subjects = admission.Nodes;
        if (subjects.Count == 0)
            return (
            [
                Violation.EmptySubject(
                    EmptySubjectMessage, AuthoringHints.ForSubject(subject!, _incompleteModel))
            ], NoWarnings, default);

        (IReadOnlyList<Violation> violations, IReadOnlyList<CheckWarning> warnings) =
            Dispatch(constraint, subjects, admission, family);
        return (violations, warnings, CoverageOf(subjects));
    }

    // The subject-coverage pair the report states (GRAMMAR §5.2): how big the materialized subject set is
    // and how much of it a generator emitted. Measured HERE, on the set the verbs actually range over,
    // rather than on the selection that produced it — which is what makes the statement self-extinguishing:
    // adding .Authored() leaves nothing generated, and the statement disappears with no separate rule about
    // when to suppress it. Both numbers, because "803 of 804" and "803 of 90,000" are different findings and
    // the denominator appears nowhere else in the document.
    private static SubjectCoverage CoverageOf(IReadOnlyCollection<TypeNode> subjects)
    {
        return new SubjectCoverage(subjects.Count, subjects.Count(type => type.IsGenerated));
    }

    // A member subject's coverage is reported in DECLARING TYPES, not members: the pair has to keep one unit
    // for "n of m" to mean anything, and the generated-ness fact is a type fact — a member of a generated
    // type is generated code whether or not it is counted as one.
    private static SubjectCoverage MemberCoverageOf(IReadOnlyList<MemberNode> members)
    {
        var declaringTypes = new HashSet<ITypeInfo>(members.Select(member => member.DeclaringType));
        return new SubjectCoverage(declaringTypes.Count, declaringTypes.Count(type => type.IsGenerated));
    }

    // The verb dispatch, kept apart from Evaluate so the coverage pair can ride beside the pair every
    // arm returns. No arm knows about coverage — the subject set it is measured from is already resolved.
    // Every EDGE verb reads the admission rather than the bare set: the subject bounds which of an edge's
    // per-declarer instances the rule owns, at whichever end it sits (§4.1). The member path is one of them,
    // since a member use is an edge with a name-keyed far end. The shape verbs take the set, because a rule
    // with no edge has nothing to attribute.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Dispatch(
        Constraint constraint, HashSet<TypeNode> subjects, SelectionAdmission admission, FamilySubject? family)
    {
        switch (constraint)
        {
            case MustNotReferenceConstraint c:
                return ForbiddenReference(admission, c.Targets, inbound: false);
            case MustNotBeReferencedByConstraint c:
                return ForbiddenReference(admission, c.Sources, inbound: true);
            case MustOnlyReferenceConstraint or MustOnlyReferenceItselfConstraint:
                // The leaf verb rides the allow-list's own arm with the empty operand list it was built to
                // carry: an empty admission, which the implicit self-allowance folds back to the subject alone.
                return OnlyReference(admission, constraint.Operands, family);
            case MustOnlyBeReferencedByConstraint or MustOnlyBeReferencedByItselfConstraint:
                // The inbound pair, likewise: the leaf's empty operand list resolves to the subject alone,
                // and MustOnlyBeReferencedBy's Sources IS the inherited operand list.
                return OnlyBeReferencedBy(admission, constraint.Operands, family);
            case MustNotReferenceEachOtherConstraint:
                return EachOther(family);
            case MustNotHaveCircularReferencesConstraint:
                return CircularReferences(family);
            case MustNotUseConstraint c:
                return ForbiddenMemberUse(admission, c.MemberOperands);
            case MustNotConstructConstraint c:
                return ForbiddenConstruction(admission, c.Targets);
            case MustNotInjectConstraint c:
                return ForbiddenInjection(admission, c.Targets);
            case MustNotCatchConstraint c:
                return ForbiddenCatch(admission, c.Targets);
            case MustNotCatchUnfilteredConstraint c:
                return ForbiddenUnfilteredCatch(admission, c.Targets);
            case MustNotSwallowConstraint c:
                return ForbiddenSwallow(admission, c.Targets);
            case MustNotExposeConstraint c:
                return ForbiddenExposure(admission, c.Targets);
            case MustOnlyThrowConstraint c:
                return OnlyThrow(admission, c.Targets);
            case MustNotThrowConstraint c:
                return ForbiddenThrow(admission, c.Targets);
            case MustResideInNamespaceConstraint c:
                var namespacePattern = new NamespacePattern(c.Glob);
                return Resided(
                    Shape(subjects, t => namespacePattern.Matches(t.Namespace)),
                    new RefinedSelection(c.Subject!.Owner, new NamespaceNoun(c.Glob), Array.Empty<SelectionAdjective>()));
            case MustResideInProjectConstraint c:
                // The N-way declarer predicate, never ProjectName equality: one source file compiled into
                // several projects resides in every one of them (GRAMMAR §4.1).
                return Resided(
                    Shape(subjects, t => t.IsDeclaredBy(c.ProjectName)),
                    new RefinedSelection(c.Subject!.Owner, new ProjectNoun(c.ProjectName), Array.Empty<SelectionAdjective>()));
            case MustBelongToConstraint c:
                // Memberships resolve in SUBJECT position — they name where a subject may live, not an
                // edge's far end — and the test is plain membership, because there is no edge to attribute.
                SelectionAdmission membership = SelectionAdmission.Operands(
                    _selections, c.Memberships, SelectionPosition.Subject, out IReadOnlyList<SelectionAdmission> memberships);
                (IReadOnlyList<Violation> outside, _) = Shape(subjects, membership.Contains);
                return (outside, SubjectSideDeadWarnings(c.Memberships, memberships, DeadOperandRole.Membership));
            case MustHaveExactlyOneCounterpartConstraint c:
                return Counterparts(subjects, c);
            case MustBeRegisteredConstraint:
                HashSet<TypeNode> registered = _selections.RegisteredMembers(SelectionPosition.Subject);
                return Shape(subjects, registered.Contains);
            case MustHaveSuffixConstraint c:
                return Shape(subjects, t => t.Name.EndsWith(c.Suffix, StringComparison.Ordinal));
            case MustHavePrefixConstraint c:
                return Shape(subjects, t => t.Name.StartsWith(c.Prefix, StringComparison.Ordinal));
            case MustHaveNameMatchingConstraint c:
                var namePattern = new TypeNamePattern(c.Glob);
                return Shape(subjects, t => namePattern.Matches(t.Name));
            case MustImplementConstraint c:
                return Shape(subjects, SelectionEvaluator.InterfaceMatcher(c.Anchor));
            case MustDeriveFromConstraint c:
                return Shape(subjects, SelectionEvaluator.BaseTypeMatcher(c.Anchor));
            case MustBeAttributedWithConstraint c:
                return Shape(subjects, SelectionEvaluator.AttributeMatcher(c.Anchor));
            case MustNotImplementConstraint c:
                return Shape(subjects, NoneOf(c.Anchors, SelectionEvaluator.InterfaceMatcher));
            case MustNotDeriveFromConstraint c:
                return Shape(subjects, NoneOf(c.Anchors, SelectionEvaluator.BaseTypeMatcher));
            case MustNotBeAttributedWithConstraint c:
                return Shape(subjects, NoneOf(c.Anchors, SelectionEvaluator.AttributeMatcher));
            case MustBeSealedConstraint:
                return Shape(subjects, t => t.IsSealed);
            case MustBeStaticConstraint:
                return Shape(subjects, t => t.IsStatic);
            case MustBeAbstractConstraint:
                return Shape(subjects, t => t.IsAbstract);
            case MustBePublicConstraint:
                return Shape(subjects, t => t.Accessibility == Accessibility.Public);
            case MustBeInternalConstraint:
                return Shape(subjects, t => t.Accessibility == Accessibility.Internal);
            case MustConstraint c:
                return Shape(subjects, t => SelectionEvaluator.InvokePredicate(c.Predicate, t, "Must"));
            default:
                // Fail closed: the closed Constraint hierarchy makes this arm unreachable for any v1
                // verb, so an unknown subclass means a new verb shipped without a switch arm. Surface it
                // loudly — ArchChecker.Verdict contains the throw as a per-rule RuleError — rather than
                // passing the rule silently (a missing arm must never read green).
                throw new InvalidOperationException($"Unhandled constraint '{constraint.GetType().Name}'.");
        }
    }

    /// <summary>
    ///     The one walk behind every forbidden-set verb (GRAMMAR §4.1, §4.3, §4.5, §4.7, §4.8, §4.9):
    ///     resolve the operands, ask each candidate edge which of its sites count against the rule, mint
    ///     one violation per counting edge from exactly those sites, and — for
    ///     the arms that warn — raise the inert-target warnings, whole-target or per operand (InertWarnings
    ///     says when each fires). <c>requireSites</c> is the refinement
    ///     the two catch subsets carry: an edge none of whose sites the ban forbids is green, and no
    ///     printed <c>file:line</c> is ever a site the ban permits.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <paramref name="subject" /> rides beside the candidates because the §4.1 edge rule needs
    ///         both ends of the same test: the subject bounds which of the edge's instances the rule owns,
    ///         and the operand set decides the far end at each of them. <paramref name="evidenceOf" /> reads
    ///         the evidence each edge family recorded for itself, since a copy can construct or expose
    ///         what another copy only references. Which end the subject sits at is
    ///         <paramref name="subjectAtSource" /> — true for every verb here but the inbound reference one.
    ///     </para>
    ///     <para>
    ///         The evidence is the sites and the instances' shares of them as one value, and it is what
    ///         makes the printed lines the owned copies' own: a verb deciding on one of a catch edge's
    ///         narrowings passes that narrowing's evidence whole, so what it mints from is a share of the
    ///         list it is citing. <c>requireSites</c> then tests the counted sites rather than the whole
    ///         edge, which is what keeps an unfiltered clause made by a copy the subject does not own from
    ///         minting a violation with nothing to point at.
    ///     </para>
    ///     <para>
    ///         The live trap for the inbound arm: its operand admission is resolved in
    ///         <see cref="SelectionPosition.Target" /> and then plays the edge's SOURCE role. Benign,
    ///         because target position differs only by admitting external nodes and an edge source is
    ///         always solution-declared, but it is not self-evident from the call.
    ///     </para>
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenEdge<TEdge>(
        IEnumerable<TEdge> candidates,
        SelectionAdmission subject,
        IReadOnlyList<Selection> operands,
        Func<TEdge, EdgeEvidence> evidenceOf,
        Func<TypeNode, TypeNode, IReadOnlyList<SourceLocation>, Violation> toViolation,
        bool subjectAtSource,
        bool requireSites,
        bool warnInert)
        where TEdge : IEdgeEnds
    {
        SelectionAdmission operandSet = ResolveOperands(operands, out IReadOnlyList<SelectionAdmission> parts);
        var violations = new List<Violation>();

        foreach (TEdge edge in candidates)
        {
            IReadOnlyList<SourceLocation>? sites = SelectionAdmission.CountedSites(
                subject, operandSet, edge, evidenceOf(edge), subjectAtSource, wantHit: true);

            if (sites is null) continue;
            if (requireSites && sites.Count == 0) continue;

            violations.Add(toViolation(edge.Source, edge.Target, sites));
        }

        return (violations, InertWarnings(operands, parts, operandSet, violations.Count, warnInert));
    }

    // The inert-target report, at both grains. The whole-target arm answers an empty folded set; the
    // per-operand arm fires only where the folded set is NON-empty, which is exactly the case the
    // whole-target arm can never see, so the two never fire for the same rule. The per-operand arm also
    // fires whatever the live operands found, so its warning can ride a failed rule.
    //
    // Each grain raises its own kind. Everything downstream reads the whole-target kind as "this rule
    // measured nothing" — the ratchet counts, the baseline verb, promotion — and a rule whose other
    // operands matched measured those.
    //
    // Only a PATTERN operand warns, at either grain: a bare typeof target absent from the codebase is
    // the win condition (GRAMMAR §4.1) and stays silent, because the compiler already checked the name.
    private IReadOnlyList<CheckWarning> InertWarnings(
        IReadOnlyList<Selection> operands,
        IReadOnlyList<SelectionAdmission> parts,
        SelectionAdmission operandSet,
        int violationCount,
        bool warnInert)
    {
        if (!warnInert) return NoWarnings;

        if (operandSet.Count == 0)
            return violationCount == 0 && operands.Any(SelectionEvaluator.IsPatternSelection)
                ?
                [
                    CheckWarning.InertTarget(
                        InertTargetMessage, AuthoringHints.ForInertTarget(operands, _incompleteModel, _narrowing))
                ]
                : NoWarnings;

        // A dead operand beside a live one. The fold hides it — the united set is non-empty, so the
        // rule reads as guarding everything it names while one clause of the ban has gone quiet. The
        // subject side names this grain per operand (GRAMMAR §4.1); this is the target twin.
        return DeadOperandWarnings(operands, parts, DeadOperandRole.Target, SelectionPosition.Target);
    }

    // The per-operand grain every permit or ban list reports at: each pattern operand that matched nothing,
    // and below a live one each dead piece DeadPieces finds. A forbidden set reaches here only where its
    // folded set is non-empty. An allow-list always does, and never raises the whole-target kind: an empty
    // allow-set reds every edge, so the rule measured something and must not read as having measured
    // nothing.
    private IReadOnlyList<CheckWarning> DeadOperandWarnings(
        IReadOnlyList<Selection> operands,
        IReadOnlyList<SelectionAdmission> parts,
        DeadOperandRole role,
        SelectionPosition position)
    {
        List<CheckWarning>? warnings = null;
        for (var i = 0; i < parts.Count && i < operands.Count; i++)
            foreach (Selection piece in DeadPieces(operands[i], parts[i], position))
                AddOnce(ref warnings, piece, role);

        return warnings ?? NoWarnings;
    }

    // The subject-position lists — memberships, the reside pair's home, and among: — read at the same grain,
    // and silent under any narrowing, as the exemplar is: a run over part of the solution cannot tell a dead
    // pattern from one whose types sit in a project it never loaded, and these lists are never cut down to
    // the run's projects the way the rule subject is.
    private IReadOnlyList<CheckWarning> SubjectSideDeadWarnings(
        IReadOnlyList<Selection> operands, IReadOnlyList<SelectionAdmission> parts, DeadOperandRole role)
    {
        return _narrowing is not null
            ? NoWarnings
            : DeadOperandWarnings(operands, parts, role, SelectionPosition.Subject);
    }

    // The reside pair warns exactly as its MustBelongTo form would: the glob or the project name is minted
    // into the selection that form names and read as one membership, while the verdict stays the pair's own
    // predicate.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Resided(
        (IReadOnlyList<Violation> Violations, IReadOnlyList<CheckWarning> Warnings) verdict, Selection home)
    {
        SelectionAdmission.Operands(
            _selections, [home], SelectionPosition.Subject, out IReadOnlyList<SelectionAdmission> parts);
        return (verdict.Violations, SubjectSideDeadWarnings([home], parts, DeadOperandRole.Membership));
    }

    // What of one operand, or of one part of one, is dead: the whole of it where it matched nothing and is a
    // pattern (a typeof that matched nothing stays silent). Below a live one, each part of a union and the
    // definition of a layer is collected raw, in the position of the list it came from and before the
    // enclosing selection's adjectives (the subject gate's reading), and read the same way in turn; then
    // each dead name of the operand's own lists. Parts come before the operand's lists because they are
    // written first: the adjectives of `AnyOf(a, b).Named(...)` follow its parts.
    //
    // Re-collecting a part costs no noun scan, since those are memoized, only its own adjectives again; and
    // OpenedParts opens a layer only where something inside its definition can be dead, so a layer defined by
    // a project or a namespace costs nothing here. The rule subject's own gate leaves a layer's definition
    // folded: a per-name failure there would have to be dropped by the one-project cut too, which would mean
    // minting a layer with a different definition. A membership or an among part is never cut that way, so
    // it may open one here.
    private IEnumerable<Selection> DeadPieces(Selection selection, SelectionAdmission matched, SelectionPosition position)
    {
        if (matched.Count == 0)
        {
            if (SelectionEvaluator.IsPatternSelection(selection)) yield return selection;
            yield break;
        }

        if (OpenedParts(selection) is { } parts)
            foreach (Selection part in parts)
            {
                SelectionAdmission partMatched = SelectionAdmission.Collect(_selections, part, position);
                foreach (Selection inner in DeadPieces(part, partMatched, position))
                    yield return inner;
            }

        foreach (Selection deadName in DeadNames.In(selection, matched.Nodes))
            yield return deadName;
    }

    // The parts DeadPieces opens: a union's always, since any of them can be empty; a layer's definition only
    // where it is compound itself or carries a list of its own, since otherwise it is dead exactly when the
    // layer is.
    private IReadOnlyList<Selection>? OpenedParts(Selection selection)
    {
        if (selection is UnionSelection union) return union.Parts;

        return selection.Noun is LayerNoun { Definition: { } definition }
               && (_selections.Composite(definition) is not null || DeadNames.Possible(definition))
            ? [definition]
            : null;
    }

    // One warning per rendered reference, which is what every surface downstream keys on: two warnings
    // saying the same thing would be two alerts with one fingerprint. Each piece takes the advice for its own
    // shape, a dead name spelled alone the exact-name cure, so a name and a one-name operand saying the same
    // thing carry the same cure whichever comes first.
    private void AddOnce(ref List<CheckWarning>? warnings, Selection piece, DeadOperandRole role)
    {
        string reference = SentenceRenderer.Reference(piece);
        warnings ??= [];
        bool raised = warnings.Exists(warning =>
            warning.Kind == CheckWarningKind.DeadOperand && warning.Key == reference);
        if (raised) return;

        CheckWarning warning = role switch
        {
            DeadOperandRole.Target => CheckWarning.InertTargetOperand(
                reference, AuthoringHints.ForInertTargetOperand(piece, _incompleteModel, _narrowing)),
            DeadOperandRole.AllowList => CheckWarning.DeadAllowedOperand(
                reference, AuthoringHints.ForDeadAllowedOperand(piece, _incompleteModel, _narrowing)),
            DeadOperandRole.Membership => CheckWarning.DeadMembership(
                reference, AuthoringHints.ForDeadMembership(piece, _incompleteModel)),
            DeadOperandRole.Among => CheckWarning.DeadAmongOperand(
                reference, AuthoringHints.ForDeadAmongOperand(piece, _incompleteModel)),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Not a dead operand's role.")
        };
        warnings.Add(warning);
    }

    // The candidate edges of one kind for a subject-keyed walk: only those whose keyed endpoint is a
    // subject, read off the per-kind index rather than by re-walking the whole edge list (the reference
    // list is the biggest the extractor produces, and a re-walk costs every rule O(|edges|)). Each edge sits
    // under exactly one key and `subjects` is a set, so no edge is yielded twice. Walk order is the
    // subject set's rather than the model's, which is unobservable: ArchChecker re-sorts every violation
    // list on Violation.OrderKey before it reaches a report, and edges are unique per endpoint
    // pair, so no tie-break rides on the walk.
    private static IEnumerable<TEdge> Keyed<TEdge>(HashSet<TypeNode> subjects, ILookup<TypeNode, TEdge> index)
    {
        return subjects.SelectMany(subject => index[subject]);
    }

    /// <summary>
    ///     The reference verbs (GRAMMAR §4.3). Outbound (<c>MustNotReference</c>): edge subject→operand.
    ///     Inbound (<c>MustNotBeReferencedBy</c>): edge operand→subject, so the walk keys on the target
    ///     while the violation still names <c>Source</c> — the referencing type, where the edit happens.
    /// </summary>
    /// <remarks>
    ///     The two directions are one test read from opposite ends (§4.1), which is why neither filters
    ///     the candidates first: "some instance the subject owns puts the operand at the far end" is a
    ///     claim about a declarer both ends agree on, and two independent filters cannot state it.
    ///     Inbound, the subject sits at the target and bounds ownership there while the operand decides
    ///     the source; outbound, the other way round.
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenReference(
        SelectionAdmission subject, IReadOnlyList<Selection> operands, bool inbound)
    {
        ILookup<TypeNode, ReferenceEdge> index = inbound ? _edgesByTarget.Lookup : _edgesBySource.Lookup;

        return ForbiddenEdge(
            Keyed(subject.Nodes, index), subject, operands,
            e => e.Evidence,
            Violation.Reference,
            subjectAtSource: !inbound, requireSites: false, warnInert: true);
    }

    // The member-access verb (GRAMMAR §4.5): a member edge is a hit when its source is a subject AND
    // its used member matches a banned (declaring type, name) pair — ordinal, one ban covering every
    // overload. Per-overload edges yield per-overload MemberUse violations (the §4.3 identity substrate).
    // The banned set is resolved eagerly so a closed-generic member anchor is refused (RuleError) before
    // any edge is tested, through the same SelectionEvaluator.DefinitionFullName refusal the type noun takes.
    // The two ends answer differently. The MEMBER match is keyed on names rather than on nodes, so it is
    // attribution-insensitive by construction, and it must stay that way: a (declaring type, name) string set
    // has no head to read a project off, so there is no instance for it to be asked about. That is also the
    // answer §4.1 already gives a typeof operand — a member of a type several projects compile is banned in
    // every one of them, its own copy included. The SUBJECT is an ordinary type selection, though, and owns the
    // uses of the copies it names the source at (§4.1, §4.5), as at the source end of every other edge verb: a
    // project-headed ban skips a use only another declarer's copy makes, and cites its own copy's lines alone.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenMemberUse(
        SelectionAdmission subject, IReadOnlyList<Member> members)
    {
        var banned = new HashSet<(string DeclaringType, string Name)>();
        foreach (Member member in members)
        {
            string declaringType = SelectionEvaluator.DefinitionFullName(
                member.DeclaringAnchor,
                "member-use edges are definition-level. Anchor the member on the open definition instead.");
            banned.Add((declaringType, member.Name));
        }

        var violations = new List<Violation>();
        foreach (MemberEdge edge in Keyed(subject.Nodes, _memberEdgesBySource.Lookup))
        {
            if (!banned.Contains((edge.Member.ContainingType.FullName, edge.Member.Name))) continue;

            IReadOnlyList<SourceLocation>? sites = SelectionAdmission.OwnedSites(subject, edge.Source, edge.Evidence);
            if (sites is null) continue;

            violations.Add(Violation.MemberUse(edge.Source, edge.Member, sites));
        }

        // MustNotUse never warns: member targets are concrete (type, name) anchors (no pattern form), so a banned member
        // absent from the codebase is the win condition, exactly like a bare typeof target (GRAMMAR §4.5).
        return (violations, NoWarnings);
    }

    /// <summary>
    ///     The construction verb (GRAMMAR §4.5, §5.3): a construction edge is a hit when its source is a
    ///     subject AND the constructed type is a forbidden operand — the "you may use it; you may not
    ///     create it" ban. Extraction already collapses every <c>new</c> of one type into one (source,
    ///     constructed) edge with its sites aggregated, so one edge yields one Construction violation
    ///     keyed on the type pair (overload-indifferent, §4.3).
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenConstruction(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Nodes, _constructorEdgesBySource.Lookup), subject, operands,
            e => e.Evidence,
            Violation.Construction,
            subjectAtSource: true, requireSites: false, warnInert: true);
    }

    /// <summary>
    ///     The injection verb (GRAMMAR §4.7, §5.3): an injection edge is a hit when its source is a
    ///     subject AND the injected parameter type is a forbidden operand — the captive-dependency ban.
    ///     Extraction already collapses every constructor parameter typed on one injected type into one
    ///     (source, injected) edge with its sites aggregated, so one edge yields one Injection violation
    ///     keyed on the type pair (constructor-overload- and parameter-name-indifferent, §4.3). Unlike
    ///     ForbiddenConstruction, <c>MustNotInject</c> NEVER warns — hence <c>warnInert: false</c>: its
    ///     natural operand is a Registered selection (§4.7), and an empty Registered operand means no such
    ///     registrations exist — the win condition, exactly like a bare <c>typeof</c> target (GRAMMAR
    ///     §4.1). An empty operand set is silence, not a warning.
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenInjection(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Nodes, _injectionEdgesBySource.Lookup), subject, operands,
            e => e.Evidence,
            Violation.Injection,
            subjectAtSource: true, requireSites: false, warnInert: false);
    }

    /// <summary>
    ///     The catch verb (GRAMMAR §4.8, §5.3): a catch edge is a hit when its source is a subject AND the
    ///     caught type is a forbidden operand — the "you may throw it; you may not swallow it" ban.
    ///     Matching is exact definition-level FQN on the operand set (the family's shared node
    ///     membership): <c>MustNotCatch(typeof(Exception))</c> flags only <c>catch (System.Exception)</c>
    ///     and bare-catch edges (a bare catch already synthesized System.Exception at extraction), never a
    ///     narrower <c>catch (IOException)</c>.
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenCatch(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Nodes, _catchEdgesBySource.Lookup), subject, operands,
            e => e.Evidence,
            Violation.Catch,
            subjectAtSource: true, requireSites: false, warnInert: true);
    }

    /// <summary>
    ///     The filter-aware catch verb (GRAMMAR §4.8, §5.3): the same walk as ForbiddenCatch over the same
    ///     edges, decided on the edge's recorded UNFILTERED sites instead of its sites — and on the
    ///     instances recorded for that subset rather than for the edge, since which of a copy's clauses
    ///     spell a filter is a fact about that copy. A matching edge violates iff at least one
    ///     <c>catch</c> clause an owned copy wrote spells no <c>when</c> filter, and the evidence is
    ///     exactly those clauses' sites — so an edge every one of whose owned sites is filtered is GREEN,
    ///     and no printed file:line is ever a filtered site. Extraction records the unfiltered subset as its
    ///     own fact (§4.8), so this arm reads it directly as evidence rather than subtracting one set from
    ///     another (the polarity that keeps a same-line collision red-biased). Identity is untouched — the
    ///     (source, caught) type pair, the very key ForbiddenCatch's violations carry, so the unfiltered
    ///     sites are evidence and never identity (§4.3) and a baseline entry means the same thing under
    ///     either catch verb. Matching stays exact definition-level FQN and the inert-target warning is the
    ///     §4.1 forbidden-set family's, both exactly as ForbiddenCatch.
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenUnfilteredCatch(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Nodes, _catchEdgesBySource.Lookup), subject, operands,
            e => e.UnfilteredEvidence,
            Violation.Catch,
            subjectAtSource: true, requireSites: true, warnInert: true);
    }

    /// <summary>
    ///     The rethrow-aware catch verb (GRAMMAR §4.8, §5.3): the same walk again over the same edges,
    ///     decided on the edge's recorded SWALLOWING sites and that subset's own instances — the ones that
    ///     are unfiltered AND do not end in a throw. A matching edge violates iff at least one clause an
    ///     owned copy wrote holds the failure and continues, and the evidence is exactly those sites, so an
    ///     edge whose every owned unfiltered clause rethrows or translates is GREEN and no printed
    ///     file:line is ever a rethrowing site. Extraction
    ///     records this subset as its own fact for the same polarity reason as the unfiltered subset
    ///     (§4.8) — read the sites the ban forbids, never a complement. Identity is untouched: the
    ///     (source, caught) type pair, so a baseline entry means the same thing under all three catch
    ///     verbs, the sites stay evidence (§4.3), and narrowing which sites a violation prints never
    ///     narrows what it keys. Matching stays exact definition-level FQN and the inert-target warning is
    ///     the §4.1 forbidden-set family's.
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenSwallow(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Nodes, _catchEdgesBySource.Lookup), subject, operands,
            e => e.SwallowingEvidence,
            Violation.Catch,
            subjectAtSource: true, requireSites: true, warnInert: true);
    }

    /// <summary>
    ///     The throw-ban verb (GRAMMAR §4.8, §5.3): the ban polarity beside MustOnlyThrow's strict
    ///     allow-list — a throw edge is a hit when its source is a subject AND the thrown type is a
    ///     forbidden operand, for the case where the forbidden thrown types are enumerable and the
    ///     permitted ones are not. Matching is exact definition-level FQN on the operand set, so a ban on
    ///     Exception never reaches a derived throw (the narrow throw is the good state, mirroring the catch
    ///     verbs). Inert-target warning semantics are the §4.1 forbidden-set family's, exactly as
    ///     ForbiddenCatch — the point of departure from OnlyThrow, which never raises the whole-target
    ///     warning because an empty allow-set is loud on its own, and warns per dead operand instead.
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenThrow(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Nodes, _throwEdgesBySource.Lookup), subject, operands,
            e => e.Evidence,
            Violation.Throw,
            subjectAtSource: true, requireSites: false, warnInert: true);
    }

    /// <summary>
    ///     The exposure verb (GRAMMAR §4.9, §5.3): an exposure edge is a hit when its source is a subject
    ///     AND the exposed type is a forbidden operand — the "you may use it; you may not surface it on
    ///     your public API" ban. Matching is exact definition-level FQN on the operand set (the family's
    ///     shared node membership): <c>MustNotExpose(typeof(DataTable))</c> flags only a <c>DataTable</c>
    ///     signature position, never a narrower <c>DataView</c> one (no hierarchy-aware matching).
    /// </summary>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) ForbiddenExposure(
        SelectionAdmission subject, IReadOnlyList<Selection> operands)
    {
        return ForbiddenEdge(
            Keyed(subject.Nodes, _exposureEdgesBySource.Lookup), subject, operands,
            e => e.Evidence,
            Violation.Expose,
            subjectAtSource: true, requireSites: false, warnInert: true);
    }

    /// <summary>
    ///     The outbound allow-list and its leaf form (GRAMMAR §4.1, §5.3): every reference out of the
    ///     subject whose target the allow-set does not name is a violation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The refined subject is allowed implicitly, as one more allow entry (GRAMMAR §4.1) — folded
    ///         into the resolved admission here rather than into the Targets list, so Operands, the
    ///         sentence and the diagram never see an entry no author wrote. External targets are exempt
    ///         (the complement universe is solution-declared). <c>MustOnly*</c> never raises the
    ///         whole-target warning — an empty allow-set is loud by itself, and the leaf verb's empty
    ///         operand list resolves to subject-only rather than to nothing — but each pattern operand
    ///         that matched nothing warns, since the edges it should have allowed red as breaches that
    ///         name the callers rather than the dead name. Attribution survives by flipping the polarity
    ///         <see cref="SelectionAdmission.CountedSites" /> asks with: an edge is a violation when SOME
    ///         instance the subject owns lands outside the allow-set. So an intra-copy edge is allowed by
    ///         an entry naming the compiling project — the subject counts as one, at the projects it names
    ///         the node at — or by an entry that is not a project at all, and never by an entry naming some
    ///         other declarer of the same source file.
    ///     </para>
    ///     <para>
    ///         Over a family subject "self" is the cell rather than the whole subject (GRAMMAR §5.1), so
    ///         the walk runs once per cell: the subject is that cell intersected with the family's
    ///         membership, and the allow-set is the operands plus the same cell <em>as declared</em> — the
    ///         whole layer or project, which is what lets a module's excluded <c>Contracts</c> cone still
    ///         count as its own.
    ///     </para>
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyReference(
        SelectionAdmission subject, IReadOnlyList<Selection> allowedTargets, FamilySubject? family)
    {
        return AllowList(
            subject, allowedTargets, family,
            (cell, allowed, label) => OutboundEdges(cell, allowed, wantHit: false, label, detail: null));
    }

    /// <summary>
    ///     The inbound allow-list and its leaf form: any reference into the subject from outside the
    ///     allow-set is a violation (the containment verb, GRAMMAR §7), the refined subject being one of
    ///     the allowed sources implicitly (§4.1, as for the outbound verb).
    /// </summary>
    /// <remarks>
    ///     Edge sources are always solution-declared, so no external caveat is needed. The subject sits at
    ///     the edge's TARGET end, so it bounds ownership there while the allow-set decides the source — one
    ///     test rather than two, because "some instance the subject owns comes from an unallowed project"
    ///     is a claim about one declarer and cannot be split across independent filters (§4.1). The family
    ///     path is the outbound one read from the other end: per cell, allowed = the operands plus that
    ///     cell as declared.
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyBeReferencedBy(
        SelectionAdmission subject, IReadOnlyList<Selection> allowedSources, FamilySubject? family)
    {
        return AllowList(subject, allowedSources, family, Inbound);
    }

    // The allow-list shape both directions take (GRAMMAR §4.1, §5.1): the operands resolved once, the
    // subject allowed implicitly, and — over a family — one walk per cell whose allow-set is the operands
    // plus that cell as declared. Only the walk differs, which is what `walk` carries; its third argument is
    // the cell each owned edge is labelled with, and it is the LOOP index rather than anything read off the
    // edge, so the label is the cell whose law was broken at either end. That distinction is real for the
    // inbound direction alone: there the subject sits at the edge's target, so the referencing type is
    // regularly outside the family altogether and has no cell to be attributed to. The walks collect edges
    // rather than violations because a pair two cells both own is minted once, from both cells' evidence.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) AllowList(
        SelectionAdmission subject, IReadOnlyList<Selection> allowed, FamilySubject? family,
        Func<SelectionAdmission, SelectionAdmission, string?, List<OwnedEdge<ReferenceEdge>>> walk)
    {
        SelectionAdmission operands = ResolveOperands(allowed, out IReadOnlyList<SelectionAdmission> parts);
        List<OwnedEdge<ReferenceEdge>> owned;
        if (family is null)
        {
            owned = walk(subject, operands.IncludingSelf(subject), null);
        }
        else
        {
            IReadOnlyList<SelectionAdmission> declared = DeclaredCells(family);
            owned = [];
            for (var cell = 0; cell < declared.Count; cell++)
                owned.AddRange(
                    walk(family.Subjects[cell], operands.IncludingSelf(declared[cell]), family.CellName(cell)));
        }

        IReadOnlyList<CheckWarning> dead =
            DeadOperandWarnings(allowed, parts, DeadOperandRole.AllowList, SelectionPosition.Target);
        return (MintedPerEdge(owned, e => e.Sites, MintReference), dead);
    }

    /// <summary>
    ///     The cross-cell ban (GRAMMAR §5.1, §5.3): per cell, an owned outbound instance whose far end
    ///     lies in any <em>other</em> cell, as declared, is a violation. Symmetric, so one outbound walk
    ///     states the whole law, and a one-cell family passes vacuously.
    /// </summary>
    /// <remarks>
    ///     The polarity is the allow-list's flipped — <c>wantHit: true</c> over the merged other cells —
    ///     which is what keeps one edge test behind every verb at either end (§4.1). A family subject is
    ///     required by spec-build item 28, so the null arm fails closed rather than describing a shape a
    ///     built model can carry.
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) EachOther(FamilySubject? family)
    {
        if (family is null)
            throw new RuleEvaluationException(
                "`MustNotReferenceEachOther` needs a family subject (`arch.Each`); this subject declares no cells.");

        IReadOnlyList<SelectionAdmission> declared = DeclaredCells(family);
        var owned = new List<OwnedEdge<ReferenceEdge>>();
        for (var cell = 0; cell < declared.Count; cell++)
            owned.AddRange(
                OutboundEdges(
                    family.Subjects[cell], SelectionAdmission.AllBut(declared, cell), wantHit: true,
                    family.CellName(cell), detail: null));

        return (MintedPerEdge(owned, e => e.Sites, MintReference), NoWarnings);
    }

    /// <summary>
    ///     The cycle gate (GRAMMAR §5.1, §5.3): an arrow runs from cell to cell wherever some owned
    ///     reference crosses between them, the strongly connected components are taken over those arrows,
    ///     and every type pair on an arrow whose two cells share a component is a violation. A one-cell
    ///     family and any acyclic cell graph pass; an arrow into a component from outside it is green,
    ///     because it lies on no circle.
    /// </summary>
    /// <remarks>
    ///     A violation's identity is the type pair, never the cycle: a baseline keyed on circles would churn
    ///     as arrows close new circles and enumerating a component's circles is exponential, so the pair is
    ///     what keeps the baseline, the ratchet, the human report and SARIF untouched by this verb. The
    ///     consequence is that the intended direction is blamed beside the stray back-reference — both
    ///     arrows of a two-cell circle red — by design: this verb says only that a circle exists, and the
    ///     author who knows which way the cells should point writes an ordering rule instead. The circle's
    ///     cell names ride on <see cref="Violation.Detail" />, which the JSON channel alone reads.
    ///     <para>
    ///         A family of layers is required by spec-build item 29, so both throws below fail closed
    ///         rather than describing a shape a built model can carry — the projects arm doubly so, since
    ///         the build forbids circular project references and the law could never red.
    ///     </para>
    /// </remarks>
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) CircularReferences(FamilySubject? family)
    {
        if (family is null)
            throw new RuleEvaluationException(
                "`MustNotHaveCircularReferences` needs a family of layers (`arch.Each`); this subject declares no cells.");

        if (family.LayerCells is not { } layerCells)
            throw new RuleEvaluationException(
                "`MustNotHaveCircularReferences` needs a family of layers (`arch.Each`); this subject's cells are projects, which cannot have circular references.");

        IReadOnlyList<SelectionAdmission> declared = DeclaredCells(family);
        int cellCount = declared.Count;

        // The cell graph as its arrows alone: crosses[i, j] is whether cell i references cell j at all.
        // Which arrows EXIST is the whole of what the components need, and only an arrow whose two cells
        // share one is ever a violation, so existence is asked with a walk that stops at its first edge and
        // the type pairs realizing an arrow are collected below for the few arrows that red.
        var crosses = new bool[cellCount, cellCount];
        for (var source = 0; source < cellCount; source++)
        for (var target = 0; target < cellCount; target++)
            crosses[source, target] = source != target
                                      && CrossesInto(family.Subjects[source], declared[target]);

        int[] components = CellComponents.Of(cellCount, (source, target) => crosses[source, target]);
        Dictionary<int, string> circles = CircleDetails(layerCells, components);

        var owned = new List<OwnedEdge<ReferenceEdge>>();
        for (var source = 0; source < cellCount; source++)
        for (var target = 0; target < cellCount; target++)
        {
            if (!crosses[source, target] || components[source] != components[target]) continue;

            string detail = circles[components[source]];
            string cell = family.CellName(source);
            owned.AddRange(OutboundEdges(family.Subjects[source], declared[target], wantHit: true, cell, detail));
        }

        return (MintedPerEdge(owned, e => e.Sites, MintReference), NoWarnings);
    }

    // One detail sentence per component that holds a circle, naming its cells in family DECLARATION order —
    // the order the rule's own sentence names them in, so the finding and the law read alike. Grouping over
    // the cells in order is what carries that: a group keys on first appearance and holds its members in
    // encounter order. A component of one cell holds no circle and mints nothing, which is why the caller
    // skips an absent arrow before reaching in here: a cell IS in its own component, and crosses[i, i] is
    // false by construction.
    private static Dictionary<int, string> CircleDetails(IReadOnlyList<Layer> cells, int[] components)
    {
        return Enumerable.Range(0, components.Length)
            .GroupBy(cell => components[cell], cell => cells[cell].Name)
            .Select(component => (component.Key, Names: component.ToList()))
            .Where(component => component.Names.Count > 1)
            .ToDictionary(
                component => component.Key,
                component => $"circular references among the {ProseFormat.JoinReferencesAnd(component.Names)} layers");
    }

    // The outbound reference walk the allow-list, the leaf, the cross-cell ban and the cycle gate share,
    // parameterized by the polarity each asks with and by the labels each attaches. External targets are
    // exempt either way: the complement universe is solution-declared, and a cell never holds an external
    // type to be an "other" of. Each edge comes back beside the sites that counted for it, which are the
    // owned instances' and not necessarily the whole edge's, so nothing downstream has to ask twice.
    private List<OwnedEdge<ReferenceEdge>> OutboundEdges(
        SelectionAdmission subject, SelectionAdmission operand, bool wantHit, string? cell, string? detail)
    {
        var edges = new List<OwnedEdge<ReferenceEdge>>();
        foreach (ReferenceEdge edge in Keyed(subject.Nodes, _edgesBySource.Lookup))
        {
            if (edge.Target.IsExternal) continue;

            IReadOnlyList<SourceLocation>? sites = SelectionAdmission.CountedSites(
                subject, operand, edge, edge.Evidence, subjectAtSource: true, wantHit);

            if (sites is not null) edges.Add(new OwnedEdge<ReferenceEdge>(edge, sites, cell, detail));
        }

        return edges;
    }

    // The same walk asked only whether it would find anything, which is all the cell graph needs. The cycle
    // gate tests every ordered pair of cells, and on a rule that passes no arrow it finds is evidence of
    // anything, so stopping at the first edge is the difference between reading the solution's reference
    // graph and collecting a copy of it. A counted edge answers with its evidence and an uncounted one with
    // null, so existence is the null test — and on an edge that records no instance the evidence is the
    // edge's own list handed straight back, so the cheap path stays allocation-free.
    private bool CrossesInto(SelectionAdmission subject, SelectionAdmission operand)
    {
        foreach (ReferenceEdge edge in Keyed(subject.Nodes, _edgesBySource.Lookup))
            if (!edge.Target.IsExternal
                && SelectionAdmission.CountedSites(
                    subject, operand, edge, edge.Evidence, subjectAtSource: true, wantHit: true) is not null)
                return true;

        return false;
    }

    // The inbound twin, keyed on the edge's target so the subject bounds ownership where it sits.
    private List<OwnedEdge<ReferenceEdge>> Inbound(SelectionAdmission subject, SelectionAdmission allowed, string? cell)
    {
        var owned = new List<OwnedEdge<ReferenceEdge>>();
        foreach (ReferenceEdge edge in Keyed(subject.Nodes, _edgesByTarget.Lookup))
        {
            IReadOnlyList<SourceLocation>? sites = SelectionAdmission.CountedSites(
                subject, allowed, edge, edge.Evidence, subjectAtSource: false, wantHit: false);

            if (sites is not null) owned.Add(new OwnedEdge<ReferenceEdge>(edge, sites, cell, null));
        }

        return owned;
    }

    // A family's cells collected in TARGET position — each cell as declared, which is what "self" means on
    // a family (GRAMMAR §5.1). Resolved here rather than in ResolveSubject so a family rule whose verb
    // never reads the partition pays nothing for it.
    private IReadOnlyList<SelectionAdmission> DeclaredCells(FamilySubject family)
    {
        return family.Cells
            .Select(cell => SelectionAdmission.Collect(_selections, cell, SelectionPosition.Target))
            .ToList();
    }

    // One edge a walk found and the rule owns: the edge itself, the sites that counted for it — the owned
    // instances' and not necessarily the whole edge's — and the labels the mint will carry. The cell is the
    // walk's loop index and the detail the cycle gate's circle sentence; both are null on the paths that
    // have neither.
    private readonly struct OwnedEdge<TEdge>(
        TEdge edge,
        IReadOnlyList<SourceLocation> sites,
        string? cell,
        string? detail)
        where TEdge : class
    {
        internal TEdge Edge { get; } = edge;

        internal IReadOnlyList<SourceLocation> Sites { get; } = sites;

        internal string? Cell { get; } = cell;

        internal string? Detail { get; } = detail;
    }

    // One violation per edge however many cells found it (GRAMMAR §4.3), carrying every cell's evidence. No two
    // cells hold one copy, but where one source file compiles into two of them — project cells, or layers defined
    // by those projects — each holds its own copy and owns its own instance of the same edge, so the edge can be
    // found once per cell while the baseline still keys on the type pair. Merging their evidence rather than
    // keeping the first cell's alone is what keeps such a pair citing every line the rule is about: each cell
    // owns a different copy, and dropping one would silently drop that copy's lines. Keeping the FIRST cell's
    // LABEL settles which cell the edge is attributed to — the first in declaration order, the walk being in
    // that order — so the burndown's per-cell split still names one cell per pair and totals to the rule's own
    // count.
    //
    // Grouping is on the EDGE by identity rather than on the (source, target) names: one model edge per type
    // pair per family, and a model object is compared by reference throughout the checker.
    private static IReadOnlyList<Violation> MintedPerEdge<TEdge>(
        List<OwnedEdge<TEdge>> owned,
        Func<TEdge, IReadOnlyList<SourceLocation>> sitesOf,
        Func<OwnedEdge<TEdge>, Violation> mint)
        where TEdge : class
    {
        if (owned.Count < 2)
            return owned
                .Select(mint)
                .ToList();

        var firstAt = new Dictionary<TEdge, int>(owned.Count);
        var perEdge = new List<(OwnedEdge<TEdge> Found, List<IReadOnlyList<SourceLocation>>? More)>(owned.Count);
        foreach (OwnedEdge<TEdge> found in owned)
        {
            if (firstAt.TryGetValue(found.Edge, out int at))
            {
                (OwnedEdge<TEdge> first, List<IReadOnlyList<SourceLocation>>? more) = perEdge[at];
                more ??= [first.Sites];
                more.Add(found.Sites);
                perEdge[at] = (first, more);
                continue;
            }

            firstAt[found.Edge] = perEdge.Count;
            perEdge.Add((found, null));
        }

        var violations = new List<Violation>(perEdge.Count);
        foreach ((OwnedEdge<TEdge> found, List<IReadOnlyList<SourceLocation>>? more) in perEdge)
        {
            if (more is null)
            {
                violations.Add(mint(found));
                continue;
            }

            IReadOnlyList<SourceLocation> merged = SelectionAdmission.MergedEvidence(sitesOf(found.Edge), more);
            violations.Add(mint(new OwnedEdge<TEdge>(found.Edge, merged, found.Cell, found.Detail)));
        }

        return violations;
    }

    // The reference mint both allow-list directions and the cycle gate share: the circle detail rides along
    // where the gate attached one, and the cell labels the finding either way.
    private static Violation MintReference(OwnedEdge<ReferenceEdge> found)
    {
        ReferenceEdge edge = found.Edge;
        Violation violation = found.Detail is { } detail
            ? Violation.Reference(edge.Source, edge.Target, found.Sites, detail)
            : Violation.Reference(edge.Source, edge.Target, found.Sites);

        return violation.InCell(found.Cell);
    }

    // The throw verb (GRAMMAR §4.8, §5.3): a STRICT allow-list — every throw edge from a subject whose thrown
    // type is not in the allowed set is a violation, EXTERNAL thrown types included. This is OnlyReference
    // MINUS its external-target exemption: MustOnlyThrow constrains external throws too, so an unlisted
    // System.TimeoutException throw is red unless typeof(TimeoutException) is in the allow-set (a Type-sugar
    // operand resolves the external node by FQN, since the target universe includes externals). An allowed
    // type absent from the model resolves empty and allows nothing, silently where it is a typeof and with a
    // per-operand warning where it is a pattern; the whole-target warning never fires, since an empty
    // allow-set is loud by itself (the point of departure from ForbiddenCatch). The allow-set is a
    // target position like any other, so §4.1 attribution decides membership here too: a project-headed
    // entry allows a throw of a type several projects compile only at the instance whose throwing
    // compilation reached that project's copy.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) OnlyThrow(
        SelectionAdmission subject, IReadOnlyList<Selection> allowedThrows)
    {
        SelectionAdmission allowed = ResolveOperands(allowedThrows, out IReadOnlyList<SelectionAdmission> parts);
        var owned = new List<OwnedEdge<ThrowEdge>>();

        foreach (ThrowEdge edge in Keyed(subject.Nodes, _throwEdgesBySource.Lookup))
        {
            IReadOnlyList<SourceLocation>? sites = SelectionAdmission.CountedSites(
                subject, allowed, edge, edge.Evidence, subjectAtSource: true, wantHit: false);

            if (sites is not null) owned.Add(new OwnedEdge<ThrowEdge>(edge, sites, null, null));
        }

        // One walk, so each edge is here once and the merge step mints it as found; it goes through that step
        // all the same, so no path invents a second way to turn an owned edge into a violation.
        IReadOnlyList<CheckWarning> dead =
            DeadOperandWarnings(allowedThrows, parts, DeadOperandRole.AllowList, SelectionPosition.Target);
        return (MintedPerEdge(owned, e => e.Sites, MintThrow), dead);
    }

    // The throw mint, which carries neither a cell nor a circle: MustOnlyThrow has no family form.
    private static Violation MintThrow(OwnedEdge<ThrowEdge> found)
    {
        ThrowEdge edge = found.Edge;
        return Violation.Throw(edge.Source, edge.Thrown, found.Sites);
    }

    // The subject, resolved once: its membership, the heads that admitted each conflated node in it, and
    // the per-operand emptiness verdict a union subject carries (GRAMMAR §4.1). Law must load predictably, so a typo'd
    // project name inside a four-way union fails the rule in its own right instead of being silently
    // absorbed by its siblings — one never-baselinable EmptySubject violation per empty operand, in
    // operand order, and no admission at all, because a rule that fails this gate never reaches a verb and
    // must never have the union's own adjectives applied. Target position keeps the softer per-operand
    // inert-target warning instead. A non-union subject has no operands and resolves straight through; a
    // union's operands ARE the union, so collecting them here is what keeps it from being resolved twice.
    //
    // A Named list on the subject is read per name in the same register, because Named(a, b) names exactly
    // what AnyOf(Named(a), Named(b)) names and the two spellings must fail alike: each dead name is an
    // empty operand of its own, quoted as the subject would read with that name alone. This covers the
    // subject's own list, whatever its shape, and each union part's. A subject that matched nothing fails
    // whole instead, with no per-name duplicates, mirroring the target side.
    private (IReadOnlyList<Violation> Empty, SelectionAdmission? Subject, FamilySubject? Family) ResolveSubject(
        Selection subject)
    {
        (IReadOnlyList<Violation> empty, SelectionAdmission? resolved, FamilySubject? family) = subject switch
        {
            UnionSelection union => ResolveUnionSubject(union),
            { Noun: EachNoun noun } => ResolveFamilySubject(subject, noun),
            _ => (Array.Empty<Violation>(),
                SelectionAdmission.Collect(_selections, subject, SelectionPosition.Subject), null)
        };

        if (resolved is not { Count: > 0 } whole) return (empty, resolved, family);

        List<Violation> deadNames = EmptyParts(DeadNames.In(subject, whole.Nodes));
        return deadNames.Count > 0 ? (deadNames, null, null) : (empty, resolved, family);
    }

    private (IReadOnlyList<Violation>, SelectionAdmission?, FamilySubject?) ResolveUnionSubject(UnionSelection union)
    {
        (List<SelectionAdmission> operands, List<Selection> dead) = WalkParts(union.Parts);
        if (dead.Count > 0) return (EmptyParts(dead), null, null);

        return (Array.Empty<Violation>(), SelectionAdmission.Folded(_selections, union, operands), null);
    }

    // The one part walk behind every per-part emptiness report: each part collected in subject position, in
    // part order, with what of it is dead beside — the part itself where it matched nothing, else each dead
    // name of its own lists, spelled as the part with that one name. A union's operands, a family's cells and
    // an exemplar's parts are loud for the same reason and must report alike; the subject gate fails on what
    // comes back and the exemplar warns on it. Each dead part is cured on its own terms, since the parts of
    // one union need not share a shape. A family's cells carry no list of their own, so only a union part
    // ever answers per name there.
    private (List<SelectionAdmission> Collected, List<Selection> Dead) WalkParts(IReadOnlyList<Selection> parts)
    {
        var collected = new List<SelectionAdmission>(parts.Count);
        var dead = new List<Selection>();
        foreach (Selection part in parts)
        {
            SelectionAdmission matched = SelectionAdmission.Collect(_selections, part, SelectionPosition.Subject);
            collected.Add(matched);
            if (matched.Count == 0)
                dead.Add(part);
            else
                dead.AddRange(DeadNames.In(part, matched.Nodes));
        }

        return (collected, dead);
    }

    // One empty-operand failure (GRAMMAR §4.1) per dead part, quoted by its own reference. A dead name's one-name
    // spelling carries the list, so ForSubject cures it as a name.
    private List<Violation> EmptyParts(IEnumerable<Selection> dead)
    {
        return dead
            .Select(part => Violation.EmptySubject(
                EmptyOperandMessage(SentenceRenderer.Reference(part)),
                AuthoringHints.ForSubject(part, _incompleteModel)))
            .ToList();
    }

    /// <summary>
    ///     Resolves a rule's exemplar to the solution-declared types it names, ordered ordinally by full name,
    ///     with one <see cref="CheckWarningKind.DeadExemplar" /> warning per dead part (GRAMMAR §4.1's per-part
    ///     reach, in the warning register): each union operand and each name of a <c>Named</c> list that matched
    ///     nothing, or the whole exemplar where the whole did. Never a verdict. Under a solution filter the types
    ///     are resolved and no part warns, since a project the filter left out may declare the type. Throws
    ///     <see cref="RuleEvaluationException" /> where the exemplar cannot be evaluated at all; the caller turns
    ///     that into the fault form of the same warning.
    /// </summary>
    internal (IReadOnlyList<TypeNode> Types, IReadOnlyList<CheckWarning> Warnings) ResolveExemplar(Selection exemplar)
    {
        SelectionAdmission whole;
        List<Selection> dead;
        if (exemplar is UnionSelection union)
        {
            // The subject gate's union shape: the parts read one by one, then the union's own adjectives over
            // what they fold to, then the union's own lists. A union whose every part matched something and
            // whose own adjectives emptied it is dead as a whole.
            (List<SelectionAdmission> collected, dead) = WalkParts(union.Parts);
            whole = SelectionAdmission.Folded(_selections, union, collected);
            if (whole.Count > 0)
                dead.AddRange(DeadNames.In(union, whole.Nodes));
            else if (dead.Count == 0)
                dead.Add(union);
        }
        else
        {
            (List<SelectionAdmission> collected, dead) = WalkParts([exemplar]);
            whole = collected[0];
        }

        List<TypeNode> types = whole.Nodes
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
        if (_narrowing is not null || dead.Count == 0) return (types, NoWarnings);

        // One warning per distinct reference: the reference is the warning's key, so two parts that render
        // alike warn once.
        List<CheckWarning> warnings = dead
            .Select(part => (Part: part, Reference: SentenceRenderer.Reference(part)))
            .GroupBy(entry => entry.Reference, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(entry => CheckWarning.DeadExemplar(
                entry.Reference, AuthoringHints.ForExemplar(entry.Part, _incompleteModel)))
            .ToList();
        return (types, warnings);
    }

    // A family subject, resolved once for the whole rule (GRAMMAR §5.1): its cells, their memberships, and
    // the partition discipline. Overlap is fail-closed rather than silently double-judged: a copy two cells
    // both hold has two selves, and the rule cannot say which it meant.
    private (IReadOnlyList<Violation>, SelectionAdmission?, FamilySubject?) ResolveFamilySubject(
        Selection subject, EachNoun noun)
    {
        IReadOnlyList<Selection> cells = _selections.Cells(subject);
        RequireDistinctLayerCells(noun.Layers);

        (List<SelectionAdmission> collected, List<Selection> dead) = WalkParts(cells);
        if (dead.Count > 0) return (EmptyParts(dead), null, null);

        RequireDisjointLayerCells(noun.Layers, collected);
        SelectionAdmission whole = SelectionAdmission.Folded(_selections, subject, collected);

        List<SelectionAdmission> perCell = collected.Select(cell => cell.Restricted(whole.Nodes)).ToList();
        return (Array.Empty<Violation>(), whole, new FamilySubject(cells, perCell, noun.Layers));
    }

    // The degenerate overlap, caught on the names alone before any type is resolved: a layer listed twice
    // is one cell wearing two, which no membership answer could untangle. A project family cannot reach
    // this — its cells come from a resolved project list, which holds each project once.
    private static void RequireDistinctLayerCells(IReadOnlyList<Layer>? cells)
    {
        if (cells is null) return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Layer cell in cells)
            if (!seen.Add(cell.Name))
                throw new RuleEvaluationException($"The {cell.Name} layer is listed twice in the family.");
    }

    // Partition discipline over the cells as declared (GRAMMAR §5.1). Two layer cells overlap where both hold one
    // copy of a type, meaning both name it at one project (§4.1), and that copy would have two selves, so the rule
    // fails closed naming the type, both cells, and the project whose copy it is wherever the cells share only
    // some copies. So layers defined by different projects never overlap on a type both compile, while a glob
    // layer holds every copy. The cells are read before the family's own adjectives, because self and the far end
    // are read there too: an overlap an adjective hides from the subject is still an edge landing in two cells. A
    // project family needs no check, because the partition holds there by construction: each project is its own
    // cell and names a type only at its project. The report is ordinal by type then by cell pair, so one
    // overlapping type always names the same one.
    private static void RequireDisjointLayerCells(IReadOnlyList<Layer>? cells, IReadOnlyList<SelectionAdmission> declared)
    {
        if (cells is null || cells.Count < 2) return;

        // The first cell to hold each node and, only for a node a later cell holds too, every cell holding it so far.
        // Each new holder is compared with each earlier one, because two cells that share no copy of a node can each
        // share one with a third.
        var owner = new Dictionary<TypeNode, int>();
        Dictionary<TypeNode, List<int>>? holders = null;
        List<(string Type, string First, string Second, string? Project)>? overlaps = null;
        for (var cell = 0; cell < declared.Count; cell++)
            foreach (TypeNode node in declared[cell].Nodes)
            {
                if (!owner.TryGetValue(node, out int first))
                {
                    owner[node] = cell;
                    continue;
                }

                holders ??= new Dictionary<TypeNode, List<int>>();
                if (!holders.TryGetValue(node, out List<int>? earlier))
                {
                    earlier = [first];
                    holders[node] = earlier;
                }

                foreach (int held in earlier)
                    if (declared[held].SharesCopyWith(declared[cell], node, out string? project))
                    {
                        overlaps ??= [];
                        overlaps.Add((node.FullName, cells[held].Name, cells[cell].Name, project));
                    }

                earlier.Add(cell);
            }

        if (overlaps is null) return;

        (string type, string firstCell, string secondCell, string? copy) = overlaps
            .OrderBy(overlap => overlap.Type, StringComparer.Ordinal)
            .ThenBy(overlap => overlap.First, StringComparer.Ordinal)
            .ThenBy(overlap => overlap.Second, StringComparer.Ordinal)
            .First();

        string sits = copy is null
            ? $"Type `{type}` sits"
            : $"The copy of type `{type}` that project `{copy}` compiles sits";
        throw new RuleEvaluationException(
            $"{sits} in both the {firstCell} and {secondCell} cells of the family; "
            + "a family's cells must not overlap. Leave the type out of one of the two layer definitions.");
    }

    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Shape(HashSet<TypeNode> subjects, Func<TypeNode, bool> holds)
    {
        return Shape(subjects, subject => holds(subject) ? null : subject.DeclarationSites);
    }

    // The one minter for every type-subject shape violation: the verdict answers with the sites a red
    // points at, null for a pass. Most shape verbs point at the failing subject's own declaration (the
    // predicate form above); a verb whose evidence lives elsewhere answers with those sites instead.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Shape(
        HashSet<TypeNode> subjects, Func<TypeNode, IReadOnlyList<SourceLocation>?> verdict)
    {
        var violations = new List<Violation>();
        foreach (TypeNode subject in subjects)
            if (verdict(subject) is { } sites)
                violations.Add(Violation.Shape(subject, sites));

        return (violations, NoWarnings);
    }

    // The correspondence verb (GRAMMAR §5.3): per subject the template derives a name, and exactly one type
    // in the among selection must carry it. Two arms, each with its own evidence — a subject with NO
    // counterpart is sited at its own declaration, an AMBIGUOUS one at the counterparts that collide — and
    // one identity: the key is the subject's symbol ID either way, because counterparts are evidence and
    // never identity, so a grandfathered subject stays grandfathered when the arm flips. The among selection
    // resolves in SUBJECT position, like a membership: it names where a counterpart may live rather than
    // the far end of an edge. Each dead pattern of it warns per piece, as a membership's does, but not as
    // making the rule stricter: a dead part reds a subject whose only counterpart it held, and passes one it
    // would have made ambiguous, so the rule's unmatched entries are not known to be fixed.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) Counterparts(
        HashSet<TypeNode> subjects, MustHaveExactlyOneCounterpartConstraint constraint)
    {
        SelectionAdmission among = SelectionAdmission.Operands(
            _selections, constraint.Among, SelectionPosition.Subject, out IReadOnlyList<SelectionAdmission> parts);

        // Bucketed into lists rather than a lookup's groupings, because the ambiguous arm walks the same
        // counterparts a second time to site them: the bucket IS that walk's collection, so the two common
        // arms — one counterpart, or none — cost a hash probe and allocate nothing per subject.
        var byName = new Dictionary<string, List<TypeNode>>(StringComparer.Ordinal);
        foreach (TypeNode node in among.Nodes)
        {
            if (!byName.TryGetValue(node.Name, out List<TypeNode>? carrying))
            {
                carrying = new List<TypeNode>();
                byName[node.Name] = carrying;
            }

            carrying.Add(node);
        }

        (IReadOnlyList<Violation> violations, _) = Shape(subjects, subject =>
        {
            if (!byName.TryGetValue(constraint.Derive(subject.Name), out List<TypeNode>? counterparts))
                return subject.DeclarationSites;

            return counterparts.Count == 1 ? null : CounterpartSites(counterparts);
        });
        return (violations, SubjectSideDeadWarnings(constraint.Among, parts, DeadOperandRole.Among));
    }

    // The ambiguous arm's evidence: every colliding counterpart's declaration, ordered the way a report
    // prints sites. Deliberately the counterparts rather than the subject — the edit that resolves an
    // ambiguity happens at one of them, and the subject is already named by the violation itself.
    private static IReadOnlyList<SourceLocation> CounterpartSites(IEnumerable<TypeNode> counterparts)
    {
        return counterparts.SelectMany(counterpart => counterpart.DeclarationSites)
            .OrderBy(site => site.FilePath, StringComparer.Ordinal)
            .ThenBy(site => site.Line)
            .ToList();
    }

    // The negative hierarchy/attribute verbs (GRAMMAR §5.3, §5.7): a subject VIOLATES iff ANY anchor matches,
    // so it PASSES iff NONE do — the per-subject negation over the anchor list. The matchers are the same ones
    // backing the positives (SelectionEvaluator / MemberSelectionEvaluator), built once eagerly per anchor so
    // an unrepresentable anchor throws (→ RuleError) before any subject is tested, exactly as the positive
    // Shape arms do. Every anchor in every family is a TypeAnchor (typeof or definition name, GRAMMAR §5.2),
    // so the only axis left to be generic over is the subject kind: the same negation serves a type subject
    // and a member one, whose matchers differ in what they read rather than in what they are anchored on.
    //
    // This family raises no inert report, and must not: an anchor no subject matches is what a satisfied ban
    // looks like, so a misspelt anchor cannot be told apart from a working rule (GRAMMAR §4.5, §4.6).
    private static Func<TSubject, bool> NoneOf<TSubject>(
        IReadOnlyList<TypeAnchor> anchors, Func<TypeAnchor, Func<TSubject, bool>> matcher)
    {
        List<Func<TSubject, bool>> matchers = anchors.Select(matcher).ToList();
        return subject =>
        {
            for (var i = 0; i < matchers.Count; i++)
                if (matchers[i](subject))
                    return false;

            return true;
        };
    }

    // The member modal verbs (GRAMMAR §5.7): resolve the member subject, then test each surviving member
    // against the verb's shape/naming/accessibility/flag predicate — a failing member is a MemberShape
    // violation at its own declaration sites, identity keyed on its DocId (§4.6). An empty member subject
    // fails with the member-flavored message (the analog of the empty type subject). Resolution can throw
    // RuleEvaluationException (a closed-generic .Returning anchor); ArchChecker turns that into a RuleError.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>, SubjectCoverage) EvaluateMember(
        MemberConstraint constraint, HashSet<TypeNode> sourceTypes)
    {
        // MemberConstraint.Subject IS MemberSubject.Source, so the subject the gate already resolved IS
        // this member selection's source types — read from there rather than evaluated a second time. The
        // members themselves take the set alone: a member verb is shape-only, with no edge to attribute.
        IReadOnlyList<MemberNode> members = MemberSelectionEvaluator.Resolve(constraint.MemberSubject, sourceTypes);
        if (members.Count == 0)
            return (
            [
                Violation.EmptySubject(
                    EmptyMemberSubjectMessage, AuthoringHints.ForMemberSubject(_incompleteModel))
            ], NoWarnings, default);

        (IReadOnlyList<Violation> violations, IReadOnlyList<CheckWarning> warnings) = DispatchMember(constraint, members);
        return (violations, warnings, MemberCoverageOf(members));
    }

    // The member-verb dispatch, kept apart from EvaluateMember for the same reason Dispatch is.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) DispatchMember(
        MemberConstraint constraint, IReadOnlyList<MemberNode> members)
    {
        switch (constraint)
        {
            case MemberMustHaveSuffixConstraint c:
                return MemberShape(members, m => m.Name.EndsWith(c.Suffix, StringComparison.Ordinal));
            case MemberMustHavePrefixConstraint c:
                return MemberShape(members, m => m.Name.StartsWith(c.Prefix, StringComparison.Ordinal));
            case MemberMustHaveNameMatchingConstraint c:
                var namePattern = new TypeNamePattern(c.Glob);
                return MemberShape(members, m => namePattern.Matches(m.Name));
            case MemberMustBePublicConstraint:
                return MemberShape(members, m => m.Accessibility == Accessibility.Public);
            case MemberMustBeInternalConstraint:
                return MemberShape(members, m => m.Accessibility == Accessibility.Internal);
            case MemberMustBePrivateConstraint:
                return MemberShape(members, m => m.Accessibility == Accessibility.Private);
            case MemberMustBeStaticConstraint:
                return MemberShape(members, m => m.IsStatic);
            case MemberMustBeAbstractConstraint:
                return MemberShape(members, m => m.IsAbstract);
            case MemberMustBeVirtualConstraint:
                return MemberShape(members, m => m.IsVirtual);
            case MemberMustBeGetOnlyConstraint:
                // STRICT (GRAMMAR §5.7): lawful iff the property declares NO setter accessor at all. An
                // init-only setter IS a setter, so `{ get; init; }` reds — get-only is a claim about the
                // declaration, not about when the write is allowed to happen.
                return MemberShape(members, m => !m.HasSetter);
            case MemberMustBeReadonlyConstraint:
                // A const field SATISFIES the verb (GRAMMAR §5.7): const is readonly's superset — readonly,
                // static and compile-time at once — so redding one would demand something weaker than what
                // is already there.
                return MemberShape(members, m => m.IsReadOnly || m.IsConst);
            case MemberMustBeAttributedWithConstraint c:
                // The same matcher the member attribute ADJECTIVE narrows with (MemberSelectionEvaluator),
                // built once eagerly per anchor so an unrepresentable anchor throws (→ RuleError) before any
                // member is tested — the member twin of the type-side Shape arms.
                return MemberShape(members, MemberSelectionEvaluator.MemberAttributeMatcher(c.Anchor));
            case MemberMustNotBeAttributedWithConstraint c:
                return MemberShape(members, NoneOf(c.Anchors, MemberSelectionEvaluator.MemberAttributeMatcher));
            case MemberMustAcceptParameterConstraint c:
                // The anchor resolves through the SHARED definition-FQN path (SelectionEvaluator.DefinitionFullName,
                // the same helper .Returning's anchors resolve through) — so a method passes iff one declared
                // parameter's definition-level TypeFullName equals the anchor's (a non-generic anchor exactly, an
                // open-generic anchor on any construction, GRAMMAR §4.6), and a string anchor passes through
                // verbatim because it already IS that form. Resolving eagerly here is the check-time
                // closed-generic BACKSTOP for the typeof arm: a constructed anchor throws RuleEvaluationException
                // (→ RuleError) before any member is tested. (.Returning's backstop fires a step earlier, inside
                // subject resolution — a divergence observable only for a validation-bypassed empty subject.)
                string parameterAnchor = SelectionEvaluator.DefinitionFullName(
                    c.Anchor, "member parameter matching is definition-level. Anchor on the open definition instead.");
                return MemberShape(members, m => AcceptsParameter(m, parameterAnchor));
            case MemberMustConstraint c:
                return MemberShape(members, m => SelectionEvaluator.InvokePredicate(c.Predicate, m, "Must"));
            default:
                // Fail closed: as with the type-subject switch, an unhandled member verb is a missing
                // arm, not a pass — throw so it surfaces (contained per-rule by ArchChecker), never green.
                throw new InvalidOperationException($"Unhandled member constraint '{constraint.GetType().Name}'.");
        }
    }

    // The parameter scan behind MustAcceptParameter: an index walk rather than a LINQ Any, because it runs
    // once per member of the subject.
    private static bool AcceptsParameter(IMemberInfo member, string parameterAnchor)
    {
        IReadOnlyList<IParameterInfo> parameters = member.Parameters;
        for (var i = 0; i < parameters.Count; i++)
            if (parameters[i].TypeFullName == parameterAnchor)
                return true;

        return false;
    }

    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>) MemberShape(
        IReadOnlyList<MemberNode> members, Func<IMemberInfo, bool> holds)
    {
        var violations = new List<Violation>();
        foreach (MemberNode member in members)
            if (!holds(member))
                violations.Add(Violation.MemberShape(member, member.DeclarationSites));

        return (violations, NoWarnings);
    }

    // The packaging verbs (GRAMMAR §4.10): resolve the project subject, then test each surviving project
    // against the verb's artifact predicate. An empty project subject fails with the project-flavored
    // message (the analog of the empty type and member subjects). Coverage is the zero pair — it counts
    // TYPES, and a project rule materializes none, so reporting anything else would be a claim about a set
    // this rule never had.
    private (IReadOnlyList<Violation>, IReadOnlyList<CheckWarning>, SubjectCoverage) EvaluateProject(
        ProjectConstraint constraint)
    {
        IReadOnlyList<ProjectNode> subjects = ProjectSelectionEvaluator.Resolve(constraint.ProjectSubject, _projects);
        if (subjects.Count == 0)
            return (
            [
                Violation.EmptySubject(
                    EmptyProjectSubjectMessage, AuthoringHints.ForProjectSubject(_incompleteModel))
            ], NoWarnings, default);

        IReadOnlyList<Violation> violations = DispatchProject(constraint, subjects);
        return (violations, NoWarnings, default);
    }

    // The project-verb dispatch, kept apart from EvaluateProject for the same reason Dispatch is. Every arm
    // holds one honesty rule: a fact nothing evaluated is null, and unknown PASSES — a rule that redded on
    // an unevaluated project would be reporting the load's own gaps as architecture violations. No arm has
    // anything advisory to say, so the packaging stratum answers in violations alone and EvaluateProject
    // supplies the empty warning list once.
    private static IReadOnlyList<Violation> DispatchProject(
        ProjectConstraint constraint, IReadOnlyList<ProjectNode> subjects)
    {
        switch (constraint)
        {
            case MustOnlyTargetConstraint c:
                // All over an empty framework list is vacuously true, which is exactly the absent-fact pass:
                // a project nothing evaluated declares nothing to judge.
                var permitted = new HashSet<string>(c.Frameworks, StringComparer.Ordinal);
                return ProjectShape(
                    subjects,
                    project => project.TargetFrameworks.All(permitted.Contains),
                    project => project.TargetFrameworksSite);
            case MustReferenceNoPackagesConstraint:
                return ForbiddenPackages(subjects);
            case MustLockPackagesConstraint:
                // Null passes and false reds: `!= false` is the tri-state, written as the one comparison
                // that says so rather than as a two-arm test that reads like a bug.
                return ProjectShape(
                    subjects, project => project.LocksPackages != false, project => project.LocksPackagesSite);
            case MustNotBePackableConstraint:
                return ProjectShape(
                    subjects, project => project.IsPackable != true, project => project.IsPackableSite);
            case ProjectMustConstraint c:
                // No site: a predicate is a claim about the whole project, and no single declaration in it is
                // the one that failed. An unlocated violation is the honest form of that.
                return ProjectShape(
                    subjects,
                    project => SelectionEvaluator.InvokePredicate(c.Predicate, project, "Must"),
                    _ => null);
            default:
                // Fail closed: as with the type- and member-subject switches, an unhandled project verb is a
                // missing arm, not a pass — throw so it surfaces (contained per-rule by ArchChecker).
                throw new InvalidOperationException($"Unhandled project constraint '{constraint.GetType().Name}'.");
        }
    }

    // The one walk behind the packaging shape verbs: one violation per failing project, sited at whatever
    // declared the fact the verb read — which is regularly a props file above the project, and is null where
    // nothing evaluated it.
    private static IReadOnlyList<Violation> ProjectShape(
        IReadOnlyList<ProjectNode> subjects, Func<ProjectNode, bool> holds, Func<ProjectNode, SourceLocation?> siteOf)
    {
        var violations = new List<Violation>();
        foreach (ProjectNode subject in subjects)
            if (!holds(subject))
                violations.Add(Violation.ProjectShape(subject, AtSite(siteOf(subject))));

        return violations;
    }

    // MustReferenceNoPackages (GRAMMAR §4.10): one violation per DECLARED package that is not private, sited at
    // that reference's own declaration, so a project taking eight packages reports eight lines to delete rather
    // than one line saying eight. A private reference reaches neither the project's own package nor a project
    // referencing it, so it is nobody's dependency and never counts. A project declaring none passes — and so
    // does a project nothing evaluated, because an empty package list is those two states wearing one face and
    // the honest reading of both is silence.
    private static IReadOnlyList<Violation> ForbiddenPackages(IReadOnlyList<ProjectNode> subjects)
    {
        var violations = new List<Violation>();
        foreach (ProjectNode subject in subjects)
        foreach (PackageReference package in subject.PackageReferences)
            if (!package.IsPrivate)
                violations.Add(Violation.ProjectPackage(subject, package));

        return violations;
    }

    // A nullable fact site as a violation's evidence list: the one site it has, or nothing to point at.
    private static IReadOnlyList<SourceLocation> AtSite(SourceLocation? site)
    {
        return site is null ? Array.Empty<SourceLocation>() : [site];
    }

    // A verb's forbidden set or allow-list: every operand resolved in target position and folded into one
    // admission, which carries both the membership the non-edge tests read and the per-node attribution
    // an edge test needs where several projects declare one type (GRAMMAR §4.1). The per-operand
    // admissions the fold would discard come back beside it, for the per-operand report.
    private SelectionAdmission ResolveOperands(
        IReadOnlyList<Selection> operands, out IReadOnlyList<SelectionAdmission> parts)
    {
        return SelectionAdmission.Operands(_selections, operands, SelectionPosition.Target, out parts);
    }

    // One edge kind's index, built on first use so a spec that never uses (say) a catch verb never pays
    // for a catch index. Keyed on the endpoint the SUBJECT set is tested against — Source for the
    // outbound verbs, Target for the two referenced-by verbs — with the default (reference) comparer,
    // the same identity the subject's HashSet<TypeNode> tests membership with. A key is a node, not a name, and the
    // distinction is real rather than pedantic: a name a project declares and a referenced assembly also
    // supplies denotes two nodes (GRAMMAR §4.1), and a reference into one of them must not index under the
    // other. Reference identity is what keeps them apart — do not give TypeNode value equality on FullName.
    private sealed class EdgeIndex<TEdge>
    {
        private readonly IReadOnlyList<TEdge> _edges;
        private readonly Func<TEdge, TypeNode> _key;
        private ILookup<TypeNode, TEdge>? _lookup;

        internal EdgeIndex(IReadOnlyList<TEdge> edges, Func<TEdge, TypeNode> key)
        {
            _edges = edges;
            _key = key;
        }

        internal ILookup<TypeNode, TEdge> Lookup => _lookup ??= _edges.ToLookup(_key);
    }

    // A family subject resolved once per rule (GRAMMAR §5.1): the cells themselves, and the subject each
    // cell's law ranges over — that cell intersected with the family's own membership, so an Except on the
    // family narrows what every cell governs. Held rather than recomputed because four verbs read it and
    // a project family resolves its cells against the project list to find them. The cells as DECLARED are
    // not held here: only the verbs that read the cells as declared need them, and they resolve them themselves.
    private sealed class FamilySubject(
        IReadOnlyList<Selection> cells,
        IReadOnlyList<SelectionAdmission> subjects,
        IReadOnlyList<Layer>? layerCells)
    {
        /// <summary>The cells, in declaration order for a layer family and project order for a project one.</summary>
        internal IReadOnlyList<Selection> Cells { get; } = cells;

        /// <summary>Each cell intersected with the family's membership, positionally aligned with <see cref="Cells" />.</summary>
        internal IReadOnlyList<SelectionAdmission> Subjects { get; } = subjects;

        /// <summary>The declared layer cells, or null for a project family — the partition's own kind.</summary>
        internal IReadOnlyList<Layer>? LayerCells { get; } = layerCells;

        /// <summary>
        ///     The name of the cell at <paramref name="index" /> — the layer's, or the project's for a
        ///     project family — which is what a violation minted under that cell's law is labelled with.
        /// </summary>
        /// <remarks>
        ///     Read off the cell's own noun rather than held beside it: a project cell is minted as
        ///     <c>arch.Project(name)</c> would be, so the name it resolved to is already there. A cell can
        ///     be nothing else, and a noun with no name is a new cell kind whose label nobody has decided —
        ///     so it throws rather than grouping the burndown under a blank.
        /// </remarks>
        internal string CellName(int index)
        {
            return SelectionWalk.NounOf(Cells[index]) switch
            {
                LayerNoun layer => layer.Name,
                ProjectNoun project => project.Name,
                { } noun => throw new InvalidOperationException($"Unhandled family cell noun '{noun.GetType().Name}'."),
                null => throw new InvalidOperationException("A family cell is never a union.")
            };
        }
    }
}
