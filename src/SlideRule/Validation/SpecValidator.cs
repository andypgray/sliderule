using SlideRule.Fluent;
using SlideRule.Internal;
using SlideRule.Model;
using SlideRule.Prose;
using Code = SlideRule.Validation.SpecValidationErrorCode;

namespace SlideRule.Validation;

/// <summary>
///     Runs the whole GRAMMAR §8 catalog and collects <em>every</em> error in one pass.
/// </summary>
/// <remarks>
///     ID checks run over the post-desugar ID set; authored-field checks run over the original
///     registrations, keyed to the rule or scope the author wrote. Every rule/scope/member-attributed
///     error carries the offending anchor's spec-source location (GRAMMAR §8) so all-errors-at-once
///     lands each one at a <c>file:line</c> jump target; spec-wide errors (blank and duplicate layer
///     names, layer globs, layer definitions, layer purposes) stay location-free by design.
/// </remarks>
internal static class SpecValidator
{
    internal static IReadOnlyList<SpecValidationError> Validate(Arch arch)
    {
        var errors = new List<SpecValidationError>();
        ValidateLayers(arch, errors);
        ValidateIds(arch, errors);

        foreach (Registration registration in arch.Registrations)
            switch (registration)
            {
                case RuleRegistration rule:
                    ValidateRule(rule, arch, errors);
                    break;
                case ScopeRegistration scope:
                    ValidateScope(scope, arch, errors);
                    break;
            }

        return errors;
    }

    private static void ValidateLayers(Arch arch, List<SpecValidationError> errors)
    {
        List<string> names = arch.Layers.Select(registration => registration.Noun.Name).ToList();
        Dictionary<int, CollisionVerdict> duplicates = SpecLayerNames.Decide(names)
            .ToDictionary(verdict => verdict.Tags[0]);

        for (var index = 0; index < arch.Layers.Count; index++)
        {
            LayerRegistration registration = arch.Layers[index];
            LayerNoun layer = registration.Noun;

            // Every layer error is spec-wide (null ID, named by layer in the message) and location-free: a
            // layer captures no spec-source position, and its name is a unique, trivially greppable string.
            var target = new ErrorTarget(null, null, $"layer '{layer.Name}'");

            // Blank and duplicate in one pass rather than two, so the two report in authoring order rather
            // than every blank before every duplicate. A blank name draws only the blank verdict, because
            // SpecLayerNames holds blanks out of the set it collides.
            if (!CheckLiteral(layer.Name, SpecSlot.LayerName, target, errors)
                && duplicates.TryGetValue(index, out CollisionVerdict duplicate))
                errors.Add(new SpecValidationError(duplicate.Code, null, duplicate.Sentence));

            // A layer's definition is validated here — its authoritative, use-independent home — so a bad
            // one is caught whether or not the layer is ever used as a subject (§8 items 10, 15–16, 19).
            // A selection definition takes every walk a rule's selections take, in the order a rule takes
            // them.
            if (layer.Definition is { } definition)
            {
                CheckSelectionOperands([definition], target, arch, errors);
                CheckFamilies(FamilyPositions(definition, FamilyPosition.LayerDefinition), target, errors);
            }
            else
            {
                foreach (string glob in layer.Globs)
                    CheckLiteral(glob, SpecSlot.NamespacePattern, target, errors);
            }

            // A purpose is prose like any other (§8 items 5–6) and is checked where the layer is declared, on the
            // same spec-wide terms as its globs: null ID, no location, named by layer.
            foreach (string purpose in registration.Purposes)
                CheckLiteral(purpose, SpecSlot.Purpose, target, errors);
            CheckRepeated(registration.Purposes.Count, "Purpose", target, errors);
        }
    }

    private static void ValidateIds(Arch arch, List<SpecValidationError> errors)
    {
        IReadOnlyList<Registration> registrations = arch.Registrations;

        // Malformed ID — walked per registration (not over the flattened ID list) so the offending anchor's
        // location rides along. The registration's position is also the tag the collision walk hands back,
        // which is how a collision finds the anchor it renders at.
        var declarations = new List<IdDeclaration>();
        for (var index = 0; index < registrations.Count; index++)
        {
            Registration registration = registrations[index];
            bool isScope = registration is ScopeRegistration;
            var target = new ErrorTarget(registration.Id, registration.Location);
            SpecSlot slot = isScope ? SpecSlot.ScopeId : SpecSlot.RuleId;
            IdRole role = isScope ? IdRole.Scope : IdRole.Rule;

            CheckLiteral(registration.Id, slot, target, errors);
            declarations.Add(new IdDeclaration(registration.Id, role, index));
        }

        // The extends-scope and duplicate verdicts (§8 items 1 and 7), in the order the catalog reports
        // them. Each renders at the anchor its first tag names: the rule that reaches into the reserved
        // namespace, or the first authored occurrence of a duplicated ID. The key is already the ID the
        // error is addressed to, desugared children included.
        foreach (CollisionVerdict collision in SpecIdCollisions.Decide(declarations))
        {
            Registration site = registrations[collision.Tags[0]];
            errors.Add(new SpecValidationError(collision.Code, collision.Key, collision.Sentence, site.Location));
        }
    }

    private static void ValidateRule(RuleRegistration rule, Arch arch, List<SpecValidationError> errors)
    {
        if (rule.Posture == null)
        {
            errors.Add(new SpecValidationError(Code.MissingPosture, rule.Id,
                $"Rule '{rule.Id}' has no posture; call .Enforce(...) or .Migrate(...).", rule.Location));
            return;
        }

        if (rule.PostureCount > 1)
            errors.Add(new SpecValidationError(Code.RepeatedPosture, rule.Id,
                $"Rule '{rule.Id}' has more than one posture; call .Enforce(...) or .Migrate(...) exactly once.", rule.Location));

        var target = new ErrorTarget(rule.Id, rule.Location);

        CheckBecause(rule.Becauses, target, errors);
        CheckRepeated(rule.Fixes.Count, "Fix", target, errors);
        CheckRepeated(rule.Citations.Count, "Citation", target, errors);
        CheckRepeated(rule.Exemplars.Count, "Exemplar", target, errors);
        CheckRepeated(rule.Baselines.Count, "Baseline", target, errors);
        CheckRepeated(rule.Policies.Count, "WhileYoureThere", target, errors);

        foreach ((SpecSlot slot, string? value) in RuleProse(rule))
            CheckLiteral(value, slot, target, errors);

        List<Selection> selections = RuleSelections(rule).ToList();

        CheckForeign(selections, target, arch, errors);
        CheckForeignProjects(rule, target, arch, errors);
        CheckProjectPatterns(rule, target, errors);
        CheckMembers(rule, target, arch, errors);
        CheckMemberReturning(rule, errors);
        CheckMemberAcceptParameter(rule, errors);
        CheckHierarchyAnchors(rule, errors);
        CheckPatterns(RulePatterns(rule), target, errors);
        CheckLifetimes(selections, target, errors);
        CheckFamilies(RuleFamilyPositions(rule), target, errors);
        CheckEachOtherSubject(rule, errors);
        CheckCircularReferencesSubject(rule, errors);
    }

    private static void ValidateScope(ScopeRegistration scope, Arch arch, List<SpecValidationError> errors)
    {
        // The posture field, not the selection: both verbs set the two together, so either would answer, but
        // "which posture is this scope" is the question the rest of this method asks and the one item 2 reports.
        if (scope.Posture == null)
        {
            errors.Add(new SpecValidationError(Code.MissingPosture, scope.Id,
                $"Scope '{scope.Id}' has no posture; call .Quarantine(...) or .Caution(...).", scope.Location));
            return;
        }

        if (scope.PostureCount > 1)
            errors.Add(new SpecValidationError(Code.RepeatedPosture, scope.Id,
                $"Scope '{scope.Id}' has more than one posture; call .Quarantine(...) or .Caution(...) exactly once.",
                scope.Location));

        var target = new ErrorTarget(scope.Id, scope.Location);

        CheckBecause(scope.Becauses, target, errors);
        CheckRepeated(scope.Dragons.Count, "Dragons", target, errors);
        CheckRepeated(scope.DragonsDocs.Count, "DragonsDoc", target, errors);
        CheckRepeated(scope.Baselines.Count, "Baseline", target, errors);

        if (scope.BoundaryOnlyViaCount > 1)
            errors.Add(new SpecValidationError(Code.RepeatedCall, scope.Id, $"Repeated .BoundaryOnlyVia(...) on '{scope.Id}'.", scope.Location));
        else if (scope is { BoundaryOnlyViaCount: 1, Boundary.Count: 0 })
            errors.Add(new SpecValidationError(Code.EmptyBoundary, scope.Id,
                $"BoundaryOnlyVia() on '{scope.Id}' names no types; omit the call for a hermetic quarantine.", scope.Location));

        // The message names the posture the scope was declared under, because the reader's next move is to
        // find that verb in the spec and add the clause beneath it.
        if (scope.Dragons.Count == 0 && scope.DragonsDocs.Count == 0)
            errors.Add(new SpecValidationError(Code.MissingDragons, scope.Id,
                scope.Posture == Posture.Caution
                    ? $"Cautioned scope '{scope.Id}' is missing .Dragons(...) or .DragonsDoc(...)."
                    : $"Quarantined scope '{scope.Id}' is missing .Dragons(...) or .DragonsDoc(...).",
                scope.Location));

        foreach ((SpecSlot slot, string? value) in ScopeProse(scope))
            CheckLiteral(value, slot, target, errors);

        CheckSelectionOperands(ScopeOperands(scope), target, arch, errors);
        CheckFamilies(ScopeFamilyPositions(scope), target, errors);
    }

    private static void CheckBecause(List<string> becauses, ErrorTarget target, List<SpecValidationError> errors)
    {
        if (becauses.Count == 0) errors.Add(new SpecValidationError(Code.MissingBecause, target.Id, $"'{target.Id}' is missing a required .Because(...).", target.Location));

        CheckRepeated(becauses.Count, "Because", target, errors);
    }

    private static void CheckRepeated(int count, string verb, ErrorTarget target, List<SpecValidationError> errors)
    {
        if (count > 1) errors.Add(new SpecValidationError(Code.RepeatedCall, target.Id, $"Repeated .{verb}(...) on {target.Subject}.", target.Location));
    }

    // Every single-literal report the catalog makes, in one shape: the slot decides what is wrong with the
    // value and says it in the parts of a sentence, and the target frames that sentence and places the
    // error. Nothing here knows which code it is reporting, which is what keeps one slot from being
    // reported by two walks under two wordings.
    private static bool CheckLiteral(string? value, SpecSlot slot, ErrorTarget target, List<SpecValidationError> errors)
    {
        if (slot.Decide(value) is not { } verdict) return false;

        errors.Add(new SpecValidationError(verdict.Code, target.Id, verdict.On(target.Subject), target.Location));
        return true;
    }

    // The walks every selection-bearing anchor takes — a layer's definition and a scope's operands alike —
    // in the order a rule's selections take them, so a bad selection reports the same first error wherever
    // it sits.
    private static void CheckSelectionOperands(
        IEnumerable<Selection> operands, ErrorTarget target, Arch arch, List<SpecValidationError> errors)
    {
        List<Selection> roots = operands.ToList();
        List<Selection> expanded = roots.SelectMany(SelectionWalk.ExpandSelection).ToList();

        CheckForeign(expanded, target, arch, errors);
        CheckPatterns(roots.SelectMany(SelectionPatterns), target, errors);
        CheckLifetimes(expanded, target, errors);
    }

    // GRAMMAR §8 items 15–16. Every glob, affix and string anchor a rule carries, each already tagged with
    // the slot that names it in the error and owns what a literal written there can be wrong about.
    private static void CheckPatterns(
        IEnumerable<(string Value, SpecSlot Slot)> patterns, ErrorTarget target, List<SpecValidationError> errors)
    {
        foreach ((string value, SpecSlot slot) in patterns) CheckLiteral(value, slot, target, errors);
    }

    // Where an error lands and what its sentence calls the offender. A rule or scope error quotes its ID
    // and carries the anchor's spec-source location; a layer error is spec-wide — no ID, no location, the
    // layer named outright — which is the whole of the difference and the whole reason this is one value
    // rather than three parameters that can be threaded apart or transposed.
    private readonly struct ErrorTarget
    {
        private readonly string? _subject;

        internal ErrorTarget(string? id, SpecSourceLocation? location, string? subject = null)
        {
            Id = id;
            Location = location;
            _subject = subject;
        }

        internal string? Id { get; }

        internal SpecSourceLocation? Location { get; }

        /// <summary>The "on {subject}" text of a message: the quoted ID, or the outright name a layer supplies.</summary>
        internal string Subject => _subject ?? $"'{Id}'";
    }

    // GRAMMAR §8 item 27: a family may stand only as a rule subject. One code and one sentence shape for
    // every position, with the position word varying — what the author has to move is the family, and where
    // it stands is the whole of what they need told.
    private static void CheckFamilies(
        IEnumerable<(Selection Selection, string Position)> candidates, ErrorTarget target,
        List<SpecValidationError> errors)
    {
        foreach ((Selection selection, string position) in candidates)
        {
            if (SelectionWalk.FamilyNoun(selection) is null) continue;

            errors.Add(new SpecValidationError(Code.FamilyMisplaced, target.Id,
                $"A family (`arch.Each`) used as {position} by {target.Subject}; "
                + "a family may stand only as a rule subject.", target.Location));
        }
    }

    // GRAMMAR §8 item 28: the cross-cell ban's targets are the subject's own cells, so a subject with no
    // cells names nothing to forbid. The message names the verb the author meant, because over a plain
    // selection there is one and it is not a rewrite of the rule.
    private static void CheckEachOtherSubject(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MustNotReferenceEachOtherConstraint constraint) return;

        if (SelectionWalk.FamilyNoun(constraint.Subject) is not null) return;

        errors.Add(new SpecValidationError(Code.EachOtherWithoutFamily, rule.Id,
            $"`MustNotReferenceEachOther` on '{rule.Id}' needs a family subject (`arch.Each`); "
            + "over a plain selection write `MustNotReference`.", rule.Location));
    }

    // GRAMMAR §8 item 29: the cycle gate needs a family of LAYERS. A plain selection has no cells to be the
    // nodes of a cell graph; a family of projects has cells the build already forbids a circle among, so the
    // law would hold by construction and the rule could never red. Two wordings, because the two shapes are
    // wrong for different reasons and each has its own answer.
    private static void CheckCircularReferencesSubject(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MustNotHaveCircularReferencesConstraint constraint) return;

        if (SelectionWalk.FamilyNoun(constraint.Subject) is not { } family)
        {
            errors.Add(new SpecValidationError(Code.CircularReferencesNeedLayerFamily, rule.Id,
                $"`MustNotHaveCircularReferences` on '{rule.Id}' needs a family of layers (`arch.Each`); "
                + "over a plain selection there are no layers to reference each other.", rule.Location));
            return;
        }

        if (family.Layers is not null) return;

        errors.Add(new SpecValidationError(Code.CircularReferencesNeedLayerFamily, rule.Id,
            $"`MustNotHaveCircularReferences` on '{rule.Id}' needs a family of layers; projects cannot have "
            + "circular references, so over a family of projects the law holds by construction. Write "
            + "`MustNotReferenceEachOther` or an ordering rule.", rule.Location));
    }

    // Every position a rule's selections stand in, item 27's way: the subject itself is the one legal one,
    // so only what it NESTS is walked, while each operand is walked from its own root. A member constraint's
    // Subject is the type selection its projection was taken from, which is a subject too — a family as the
    // source of a member projection is legal.
    private static IEnumerable<(Selection Selection, string Position)> RuleFamilyPositions(RuleRegistration rule)
    {
        if (rule.Constraint is not { } constraint) yield break;

        if (constraint.Subject is { } subject)
            foreach ((Selection, string) nested in NestedFamilyPositions(subject))
                yield return nested;

        foreach (Selection operand in constraint.Operands)
        foreach ((Selection, string) position in FamilyPositions(operand, FamilyPosition.Operand))
            yield return position;

        foreach (Selection exemplar in rule.Exemplars)
        foreach ((Selection, string) position in FamilyPositions(exemplar, FamilyPosition.Exemplar))
            yield return position;
    }

    // The scope's twin: the quarantined interior and each sanctioned-surface operand, each named by the
    // position a reader would go looking for it in.
    private static IEnumerable<(Selection Selection, string Position)> ScopeFamilyPositions(ScopeRegistration scope)
    {
        // Non-null past ValidateScope's dangling-anchor return, as ScopeOperands relies on too.
        foreach ((Selection, string) position in FamilyPositions(scope.Scoped!, FamilyPosition.ScopedSelection))
            yield return position;

        foreach (Selection facade in scope.Boundary)
        foreach ((Selection, string) position in FamilyPositions(facade, FamilyPosition.Boundary))
            yield return position;
    }

    // One root and everything it nests, each paired with the position word item 27 names it by.
    private static IEnumerable<(Selection Selection, string Position)> FamilyPositions(Selection root, string position)
    {
        yield return (root, position);

        foreach ((Selection, string) nested in NestedFamilyPositions(root)) yield return nested;
    }

    // What a selection nests, and nothing else: its union parts and its Except payloads, each of which
    // consumes a set and so refuses a family. A family's own cells are Layers by construction, so there is
    // no cell arm — the type system already refused a nested family.
    private static IEnumerable<(Selection Selection, string Position)> NestedFamilyPositions(Selection root)
    {
        if (root is UnionSelection union)
            foreach (Selection part in union.Parts)
            foreach ((Selection, string) nested in FamilyPositions(part, FamilyPosition.UnionOperand))
                yield return nested;

        foreach (SelectionAdjective adjective in root.Adjectives)
            if (adjective is ExceptAdjective except)
                foreach ((Selection, string) nested in FamilyPositions(except.Payload, FamilyPosition.ExceptPayload))
                    yield return nested;
    }

    /// <summary>The position words item 27's one sentence varies on (GRAMMAR §8).</summary>
    private static class FamilyPosition
    {
        internal const string Operand = "an operand";
        internal const string ExceptPayload = "an Except payload";
        internal const string UnionOperand = "a union operand";
        internal const string LayerDefinition = "a layer definition";
        internal const string ScopedSelection = "a scoped selection";
        internal const string Boundary = "a boundary";
        internal const string Exemplar = "an exemplar";
    }

    // The foreign-Arch walk the three strata share (GRAMMAR §8 items 4, 13, 22): the first candidate some
    // other Arch minted is reported and the walk stops, because a spec assembled from two instances is one
    // mistake however many of its parts carry it. Each stratum supplies the noun its sentence opens with,
    // the code the catalog files it under, and where each candidate would land — a member steers to its own
    // arch.Member(...) call site, every other stratum to the consuming anchor's. Returns whether anything
    // was reported, which is what lets a caller skip the per-item checks a foreign owner makes meaningless.
    private static bool ReportFirstForeign(
        IEnumerable<(Arch Owner, SpecSourceLocation? Location)> candidates, Arch arch, Code code, string noun,
        ErrorTarget target, List<SpecValidationError> errors)
    {
        foreach ((Arch owner, SpecSourceLocation? location) in candidates)
        {
            if (ReferenceEquals(owner, arch)) continue;

            errors.Add(new SpecValidationError(code, target.Id,
                $"{noun} used by {target.Subject} was minted on a different Arch instance; it is not registered with this model.",
                location));
            return true;
        }

        return false;
    }

    private static void CheckForeign(
        IEnumerable<Selection> selections, ErrorTarget target, Arch arch, List<SpecValidationError> errors)
    {
        SpecSourceLocation? location = target.Location;
        IEnumerable<(Arch Owner, SpecSourceLocation? Location)> candidates =
            selections.Select(selection => (selection.Owner, Location: location));

        ReportFirstForeign(candidates, arch, Code.ForeignSelection, "A selection", target, errors);
    }

    // GRAMMAR §8 item 22: the project-stratum sibling of CheckForeign. A project selection carries its own
    // Arch (it has no underlying type selection to borrow one from), so it needs its own walk — the type-side
    // one reaches nothing on a project constraint, whose Subject is null. The walk includes the Except
    // payloads, which is the one way a project selection nests and therefore the one way a foreign one can
    // hide inside a local subject.
    private static void CheckForeignProjects(
        RuleRegistration rule, ErrorTarget target, Arch arch, List<SpecValidationError> errors)
    {
        if (rule.Constraint is null) return;

        IEnumerable<ProjectSelection> selections = SelectionWalk.ConstraintProjectSelections(rule.Constraint);
        IEnumerable<(Arch Owner, SpecSourceLocation? Location)> candidates =
            selections.Select(selection => (selection.Owner, rule.Location));

        ReportFirstForeign(
            candidates, arch, Code.ForeignProjectSelection, "A project selection", target, errors);
    }

    // GRAMMAR §8 item 23: the blank check over a project subject's own operands — the .Named names and the
    // .Matching globs. Its own code rather than the shared BlankPattern because the two shapes fail in
    // opposite directions (a blank name matches nothing, a blank glob matches everything) and the catalog
    // entry has to be able to say so. A .Named name is the one operand whose noun is shared with a slot
    // under another code, which is why it has a slot of its own: the noun a project operand is named by
    // must not drift from the noun the type-side walk names the same thing by.
    private static void CheckProjectPatterns(
        RuleRegistration rule, ErrorTarget target, List<SpecValidationError> errors)
    {
        if (rule.Constraint is null) return;

        foreach (ProjectSelection selection in SelectionWalk.ConstraintProjectSelections(rule.Constraint))
        foreach (ProjectAdjective adjective in selection.Adjectives)
            switch (adjective)
            {
                case ProjectNamedAdjective named:
                    foreach (string name in named.Names) CheckLiteral(name, SpecSlot.ProjectNameOperand, target, errors);

                    break;
                case ProjectMatchingAdjective matching:
                    foreach (string glob in matching.Globs) CheckLiteral(glob, SpecSlot.ProjectNamePattern, target, errors);

                    break;
            }
    }

    // GRAMMAR §8 items 11–13: the member-access verb's operands. A foreign member is reported once per
    // rule and stops the pass; otherwise each member is checked for a blank name and then, when named,
    // that its anchor declares it. A member error renders at the member's own arch.Member(...) call site
    // when it has one, falling back to the consuming rule's anchor for a verb-minted member (which carries
    // no location — GRAMMAR §8, items 11–13/18).
    private static void CheckMembers(
        RuleRegistration rule, ErrorTarget target, Arch arch, List<SpecValidationError> errors)
    {
        IReadOnlyList<Member> members = rule.Constraint?.MemberOperands ?? Array.Empty<Member>();
        if (members.Count == 0) return;

        IEnumerable<(Arch Owner, SpecSourceLocation? Location)> candidates =
            members.Select(member => (member.Owner, Location: member.Location ?? target.Location));
        if (ReportFirstForeign(candidates, arch, Code.ForeignMember, "A member", target, errors)) return;

        foreach (Member member in members) CheckMember(member, target, errors);
    }

    // GRAMMAR §8 item 14: a member `.Returning` anchor is definition-level, so a closed-generic anchor
    // (typeof(Task<int>)) is refused with guidance to the open definition (typeof(Task<>)). A non-generic
    // or open-generic anchor is accepted; only a MemberConstraint carries a ReturningAdjective at all. The
    // anchor is the consuming rule's statement, so it renders at the rule's location.
    // A string anchor carries no reflected type, so TypedAnchors filters it out and nothing here judges it:
    // blankness (item 15) is the whole of what a string anchor is validated for, and a constructed spelling
    // is a legal name that simply matches nothing.
    private static void CheckMemberReturning(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MemberConstraint memberConstraint) return;

        foreach (MemberAdjective adjective in memberConstraint.MemberSubject.Adjectives)
            if (adjective is ReturningAdjective returning)
                CheckClosedGenericAnchors(rule, errors, returning.Anchors, Code.MemberReturningClosedGeneric, ".Returning");
    }

    // GRAMMAR §8 item 20: a MustAcceptParameter anchor is definition-level, so a closed-generic anchor
    // (typeof(IProgress<int>)) is refused with guidance to the open definition (typeof(IProgress<>)) — the
    // sibling of the item-14 .Returning refusal, with the verb named in the steer. A non-generic or
    // open-generic anchor is accepted; only MemberMustAcceptParameterConstraint carries the anchor. The
    // anchor is the consuming rule's statement, so it renders at the rule's location. A string anchor is
    // filtered out here on the item-14 terms above.
    private static void CheckMemberAcceptParameter(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MemberMustAcceptParameterConstraint accept) return;

        CheckClosedGenericAnchors(
            rule, errors, [accept.Anchor], Code.MemberAcceptParameterClosedGeneric, "MustAcceptParameter");
    }

    // The definition-level refusal items 14 and 20 share, with the caller's verb in the steer and its own
    // code in the catalog. A string anchor carries no reflected type, so TypedAnchors drops it before
    // anything is judged.
    private static void CheckClosedGenericAnchors(
        RuleRegistration rule, List<SpecValidationError> errors, IReadOnlyList<TypeAnchor> anchors,
        Code code, string verb)
    {
        foreach (Type type in TypedAnchors(anchors))
            if (Generics.IsConstructed(type))
                errors.Add(new SpecValidationError(code, rule.Id,
                    $"'{SafeFullDisplay(type)}' is a closed generic; {verb} matches definition-level — " +
                    $"use typeof({TypeofForm(Generics.Definition(type))}) (used by '{rule.Id}').", rule.Location));
    }

    // GRAMMAR §8 item 21, both polarities — Code.HierarchyAnchorWrongCategory carries the category rule per
    // verb and why a wrong-category anchor is a silent slip.
    // A string anchor carries no reflected type, so it is filtered out (TypedAnchors) rather than guessed
    // at — in every family, hierarchy and attribute alike: there is no category to read off an FQN,
    // extraction facts are the only authority on what a name names, and refusing a spelling the host cannot
    // load would break the escape hatch's whole point. The cost is stated in the catalog: a wrong string is
    // loud on a positive (always red) and silent on a negative.
    private static void CheckHierarchyAnchors(RuleRegistration rule, List<SpecValidationError> errors)
    {
        switch (rule.Constraint)
        {
            case MustImplementConstraint c:
                CheckAnchorCategory(rule, errors, TypedAnchors([c.Anchor]), t => !t.IsInterface,
                    t => $"'{SafeFullDisplay(t)}' is not an interface; MustImplement requires an interface anchor — use MustDeriveFrom for a base class");
                break;
            case MustNotImplementConstraint c:
                CheckAnchorCategory(rule, errors, TypedAnchors(c.Anchors), t => !t.IsInterface,
                    t => $"'{SafeFullDisplay(t)}' is not an interface; MustNotImplement requires an interface anchor — use MustNotDeriveFrom for a base class");
                break;
            case MustDeriveFromConstraint c:
                CheckAnchorCategory(rule, errors, TypedAnchors([c.Anchor]), t => t.IsInterface,
                    t => $"'{SafeFullDisplay(t)}' is an interface; MustDeriveFrom requires a non-interface anchor — use MustImplement for an interface");
                break;
            case MustNotDeriveFromConstraint c:
                CheckAnchorCategory(rule, errors, TypedAnchors(c.Anchors), t => t.IsInterface,
                    t => $"'{SafeFullDisplay(t)}' is an interface; MustNotDeriveFrom requires a non-interface anchor — use MustNotImplement for an interface");
                break;
            case MustBeAttributedWithConstraint c:
                CheckAttributeAnchors(rule, errors, [c.Anchor], "MustBeAttributedWith");
                break;
            case MustNotBeAttributedWithConstraint c:
                CheckAttributeAnchors(rule, errors, c.Anchors, "MustNotBeAttributedWith");
                break;

            // The member attribute verbs (GRAMMAR §5.7) carry the identical category rule and, through
            // CheckAttributeAnchors, the identical message — the verb name in the steer is the same word on
            // either side, so a member author reads the same sentence a type author does. Only the VERBS are
            // checked — a wrong-category member attribute ADJECTIVE stays unchecked, exactly like its
            // type-side twin: it empties the subject, which the fail-on-empty gate reds loudly, whereas the
            // always-passing MustNot verb is the silent slip this item exists to catch.
            case MemberMustBeAttributedWithConstraint c:
                CheckAttributeAnchors(rule, errors, [c.Anchor], "MustBeAttributedWith");
                break;
            case MemberMustNotBeAttributedWithConstraint c:
                CheckAttributeAnchors(rule, errors, c.Anchors, "MustNotBeAttributedWith");
                break;
        }
    }

    // The attribute-anchor half of item 21, shared by the four attribute verbs (type and member, both
    // polarities): one predicate and one message template with the caller's verb name in the steer.
    private static void CheckAttributeAnchors(
        RuleRegistration rule, List<SpecValidationError> errors, IReadOnlyList<TypeAnchor> anchors, string verbName)
    {
        CheckAnchorCategory(rule, errors, TypedAnchors(anchors), t => !t.IsSubclassOf(typeof(Attribute)),
            t => $"'{SafeFullDisplay(t)}' does not derive from System.Attribute; {verbName} requires an attribute anchor");
    }

    // Walks a hierarchy verb's anchors (one for a positive, the whole list for a negative), emitting the
    // shared Code.HierarchyAnchorWrongCategory with the caller's message core plus the "(used by '{id}')" tail
    // and the rule's spec-source location — the items-19/20 convention, no new location plumbing.
    private static void CheckAnchorCategory(
        RuleRegistration rule, List<SpecValidationError> errors, IReadOnlyList<Type> anchors,
        Func<Type, bool> invalid, Func<Type, string> message)
    {
        foreach (Type anchor in anchors)
            if (invalid(anchor))
                errors.Add(new SpecValidationError(Code.HierarchyAnchorWrongCategory, rule.Id,
                    $"{message(anchor)} (used by '{rule.Id}').", rule.Location));
    }

    // The reflected arm of an anchor list — the only anchors item 21 can judge. A string anchor names a
    // definition FQN and nothing more, so it is dropped here; the asymmetry is deliberate, and the
    // blank-name check (item 15, via RulePatterns) is the whole of what a string anchor is validated for.
    private static IReadOnlyList<Type> TypedAnchors(IReadOnlyList<TypeAnchor> anchors)
    {
        return anchors.Where(anchor => anchor.Type is not null).Select(anchor => anchor.Type!).ToList();
    }

    // GRAMMAR §8 item 19: an arch.Registered noun carrying a Lifetime value outside the defined set (e.g.
    // (Lifetime)7 via a cast) names no lifetime. Walked over every selection an anchor reaches — a rule's
    // subject, operands, Except payloads, union parts and exemplar, a scope's scoped interior and sanctioned
    // surface, and a layer's definition — through the same expansion the foreign-Arch and pattern walks
    // take, so a noun cannot be checked in one position and unchecked in another. The union guard mirrors
    // SelectionPatterns (a UnionSelection has no single noun). Reported all-at-once, and the build throws
    // before membership resolution ever sees the bad value.
    private static void CheckLifetimes(
        IEnumerable<Selection> selections, ErrorTarget target, List<SpecValidationError> errors)
    {
        foreach (Selection selection in selections)
            if (SelectionWalk.NounOf(selection) is RegisteredNoun { Lifetime: { } lifetime }
                && !Enum.IsDefined(typeof(Lifetime), lifetime))
                errors.Add(new SpecValidationError(Code.UndefinedLifetime, target.Id,
                    $"'(Lifetime){(int)lifetime}' is not a defined Lifetime — " +
                    $"use Lifetime.Singleton, Lifetime.Scoped, or Lifetime.Transient (used by {target.Subject}).",
                    target.Location));
    }

    private static void CheckMember(Member member, ErrorTarget rule, List<SpecValidationError> errors)
    {
        // The member's own arch.Member(...) call site steers the diagnostic when present; a verb-minted
        // member has none, so it attributes to the consuming rule's anchor. The id stays the rule's either
        // way — a member is never the thing a spec error is addressed to.
        var target = new ErrorTarget(rule.Id, member.Location ?? rule.Location);

        // A member minted from an unresolvable anchor expression (GRAMMAR §8, item 12's expression sibling):
        // the resolver stored the diagnostic core; report it before touching the anchor. A poisoned Member's
        // anchor accessors throw if read (fail closed), so this short-circuit is the only sanctioned path.
        if (member.PoisonError is not null)
        {
            errors.Add(new SpecValidationError(Code.MemberExpressionUnresolvable, target.Id,
                $"{member.PoisonError} (used by '{target.Id}').", target.Location));
            return;
        }

        string display = SafeFullDisplay(member.DeclaringType);

        // The one slot whose subject is not the ErrorTarget's: a member is named by what declares it and by
        // the rule that reached for it, because a blank name leaves nothing else to call it.
        var named = new ErrorTarget(target.Id, target.Location, $"a member of '{display}' (used by '{target.Id}')");
        if (CheckLiteral(member.Name, SpecSlot.MemberName, named, errors)) return;

        Type anchor = Generics.Definition(member.DeclaringType);
        if (Declares(anchor, member.Name)) return;

        Type? declaringBase = FindDeclaringBase(anchor, member.Name);
        if (declaringBase != null)
        {
            errors.Add(new SpecValidationError(Code.MemberNotDeclared, target.Id,
                $"'{display}' does not declare '{member.Name}'; it is declared on base type '{SafeFullDisplay(declaringBase)}' — " +
                $"use typeof({TypeofForm(declaringBase)}) (used by '{target.Id}').", target.Location));
            return;
        }

        errors.Add(new SpecValidationError(Code.MemberNotDeclared, target.Id,
            $"'{display}' does not declare a member named '{member.Name}' (used by '{target.Id}').", target.Location));
    }

    // The first base type / interface (each normalized to its generic definition) that declares the
    // name, or null when nothing in the hierarchy declares it — GRAMMAR §8 item 12's base-type guidance.
    private static Type? FindDeclaringBase(Type anchor, string name)
    {
        if (anchor.IsInterface)
        {
            foreach (Type contract in anchor.GetInterfaces())
            {
                Type normalized = Generics.Definition(contract);
                if (Declares(normalized, name)) return normalized;
            }

            return null;
        }

        for (Type? baseType = anchor.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            Type normalized = Generics.Definition(baseType);
            if (Declares(normalized, name)) return normalized;
        }

        return null;
    }

    private static bool Declares(Type type, string name)
    {
        return DeclaredMember.Of(type, name).Length > 0;
    }

    // The C#-writable typeof operand for a (normalized) type: non-generic → bare name (`Task`); generic
    // definition → name without arity plus empty type-argument brackets (`HandlerBase<>`, `Foo<,>`).
    private static string TypeofForm(Type type)
    {
        string bare = TypeName.StripArity(type.Name);
        if (!type.IsGenericType) return bare;

        int arity = type.GetGenericArguments().Length;
        return bare + "<" + new string(',', arity - 1) + ">";
    }

    // FullDisplay of the authored type; a pointer/by-ref/partially-open anchor has no source form, so
    // fall back to Type.ToString() rather than crash validation (GRAMMAR §8 item 12).
    private static string SafeFullDisplay(Type type)
    {
        try
        {
            return TypeName.FullDisplay(type);
        }
        catch (UnrepresentableTypeException)
        {
            return type.ToString();
        }
    }

    // A rule's authored prose, each value tagged with the slot it was written in.
    private static IEnumerable<(SpecSlot Slot, string? Value)> RuleProse(RuleRegistration rule)
    {
        foreach (string because in rule.Becauses) yield return (SpecSlot.Because, because);

        foreach (string fix in rule.Fixes) yield return (SpecSlot.Fix, fix);

        if (rule.Posture == Posture.Migrate) yield return (SpecSlot.MigrateFrom, rule.MigrateFrom);

        // A path, walked as prose exactly as DragonsDoc is: blank and multi-line are the two ways to
        // write one that no later stage can report, because a blank path resolves to the solution
        // directory rather than failing — check then reads no baseline and the rule reds with nothing
        // said about why, and `baseline init` tries to write the directory itself.
        foreach (string baseline in rule.Baselines) yield return (SpecSlot.Baseline, baseline);

        if (rule.Constraint != null)
            foreach ((SpecSlot, string?) prose in ConstraintProse(rule.Constraint))
                yield return prose;

        // GRAMMAR §8 items 5, 6 and 30: a Citation is prose and a link both, and its one slot decides all three.
        foreach (string citation in rule.Citations) yield return (SpecSlot.Citation, citation);

        foreach (Selection exemplar in rule.Exemplars)
        foreach ((SpecSlot, string?) prose in SelectionProse(exemplar))
            yield return prose;
    }

    private static IEnumerable<(SpecSlot Slot, string? Value)> ScopeProse(ScopeRegistration scope)
    {
        foreach (string because in scope.Becauses) yield return (SpecSlot.Because, because);

        foreach (string dragons in scope.Dragons) yield return (SpecSlot.Dragons, dragons);

        foreach (string dragonsDoc in scope.DragonsDocs) yield return (SpecSlot.DragonsDoc, dragonsDoc);

        foreach (string baseline in scope.Baselines) yield return (SpecSlot.Baseline, baseline);

        // Non-null past ValidateScope's dangling-anchor return: the posture verbs set the selection with the
        // posture.
        foreach ((SpecSlot, string?) prose in SelectionProse(scope.Scoped!))
            yield return prose;
    }

    private static IEnumerable<(SpecSlot Slot, string? Value)> ConstraintProse(Constraint constraint)
    {
        if (constraint is MustConstraint must) yield return (SpecSlot.Description, must.Description);

        // The member escape hatches (GRAMMAR §4.6, §8 item 5 via the extended walk): the member Must
        // description and any member Where descriptions on the member subject reach BlankProse/MultiLineProse.
        if (constraint is MemberMustConstraint memberMust) yield return (SpecSlot.Description, memberMust.Description);

        if (constraint is MemberConstraint memberConstraint)
            foreach (MemberAdjective adjective in memberConstraint.MemberSubject.Adjectives)
                if (adjective is MemberWhereAdjective memberWhere)
                    yield return (SpecSlot.Description, memberWhere.Description);

        // The project escape hatches (GRAMMAR §4.10, §8 item 5 via the same extended walk): the project Must
        // description and any project Where descriptions on the project subject, including those nested under
        // its Except payloads.
        if (constraint is ProjectMustConstraint projectMust) yield return (SpecSlot.Description, projectMust.Description);

        foreach (ProjectSelection selection in SelectionWalk.ConstraintProjectSelections(constraint))
        foreach (ProjectAdjective adjective in selection.Adjectives)
            if (adjective is ProjectWhereAdjective projectWhere)
                yield return (SpecSlot.Description, projectWhere.Description);

        // For a member constraint, Subject is the underlying type selection (Subject => MemberSubject.Source),
        // so this also walks any type-side Where/Except used before the projection. Null for a project
        // constraint, whose subject is not a type selection at all — the loop above is its walk.
        if (constraint.Subject is { } subject)
            foreach ((SpecSlot, string?) prose in SelectionProse(subject))
                yield return prose;

        foreach (Selection operand in constraint.Operands)
        foreach ((SpecSlot, string?) prose in SelectionProse(operand))
            yield return prose;
    }

    private static IEnumerable<(SpecSlot Slot, string? Value)> SelectionProse(Selection selection)
    {
        // A union's operands answer for its noun; its own adjective list is walked after, like any other
        // selection's — AnyOf(a, b).Where(p, "") must still reach the blank-prose check (§8 item 5).
        if (selection is UnionSelection union)
            foreach (Selection member in union.Parts)
            foreach ((SpecSlot, string?) prose in SelectionProse(member))
                yield return prose;

        foreach (SelectionAdjective adjective in selection.Adjectives)
            if (adjective is WhereAdjective where)
                yield return (SpecSlot.Description, where.Description);
            else if (adjective is ExceptAdjective except)
                foreach ((SpecSlot, string?) prose in SelectionProse(except.Payload))
                    yield return prose;
    }

    // The constraint's selections, then the exemplar's: an exemplar is spec-authored selection like any
    // other, so the foreign-Arch and lifetime walks reach it, though the verdict never reads it.
    private static IEnumerable<Selection> RuleSelections(RuleRegistration rule)
    {
        IEnumerable<Selection> constraintSelections = rule.Constraint == null
            ? Enumerable.Empty<Selection>()
            : SelectionWalk.ConstraintSelections(rule.Constraint);
        IEnumerable<Selection> exemplarSelections = rule.Exemplars.SelectMany(SelectionWalk.ExpandSelection);
        return constraintSelections.Concat(exemplarSelections);
    }

    // The glob/affix walk (GRAMMAR §8 items 15–16), parallel to the prose and selection walks: every
    // glob and affix a rule carries, each tagged with the slot that names it in the error and owns what a
    // literal written there can be wrong about — the full structural check for a namespace glob, the blank
    // check alone for every other kind. A string attribute anchor rides this walk too: blank is the only
    // well-formedness a definition FQN has, so it joins the plain kinds under its own label.
    private static IEnumerable<(string Value, SpecSlot Slot)> RulePatterns(RuleRegistration rule)
    {
        IEnumerable<(string, SpecSlot)> constraintPatterns = rule.Constraint == null
            ? Enumerable.Empty<(string, SpecSlot)>()
            : ConstraintPatterns(rule.Constraint);
        IEnumerable<(string, SpecSlot)> exemplarPatterns = rule.Exemplars.SelectMany(SelectionPatterns);
        return constraintPatterns.Concat(exemplarPatterns);
    }

    // A scope's selections are the scoped interior and every operand of its sanctioned surface: the
    // boundary is spec-authored selection like any other, so the foreign-Arch, lifetime and pattern walks
    // must reach it. The desugared containment rule expands the same selections, but a scope whose spec is
    // invalid never reaches desugaring, so the error has to be found here.
    private static IEnumerable<Selection> ScopeOperands(ScopeRegistration scope)
    {
        // Non-null past ValidateScope's dangling-anchor return: the posture verbs set the selection with the
        // posture.
        yield return scope.Scoped!;

        foreach (Selection facade in scope.Boundary) yield return facade;
    }

    private static IEnumerable<(string Value, SpecSlot Slot)> ConstraintPatterns(Constraint constraint)
    {
        // The verb's own glob/affix (the shape/naming verbs carry one; dependency and boolean verbs none).
        switch (constraint)
        {
            case MustResideInNamespaceConstraint c:
                yield return (c.Glob, SpecSlot.NamespacePattern);
                break;
            case MustResideInProjectConstraint c:
                yield return (c.ProjectName, SpecSlot.ProjectName);
                break;
            case MustHaveNameMatchingConstraint c:
                yield return (c.Glob, SpecSlot.NamePattern);
                break;
            case MustHaveSuffixConstraint c:
                yield return (c.Suffix, SpecSlot.Suffix);
                break;
            case MustHavePrefixConstraint c:
                yield return (c.Prefix, SpecSlot.Prefix);
                break;
            case MemberMustHaveNameMatchingConstraint c:
                yield return (c.Glob, SpecSlot.MemberNamePattern);
                break;
            case MemberMustHaveSuffixConstraint c:
                yield return (c.Suffix, SpecSlot.MemberSuffix);
                break;
            case MemberMustHavePrefixConstraint c:
                yield return (c.Prefix, SpecSlot.MemberPrefix);
                break;

            // The attribute verbs' string anchors (GRAMMAR §8 item 15, the "attribute name" label): a blank
            // name is a slip that would otherwise mint an anchor matching nothing, silently. Only the string
            // arm has a name to check; a typeof anchor yields nothing here.
            case MustBeAttributedWithConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns([c.Anchor], SpecSlot.AttributeName)) yield return pattern;
                break;
            case MustNotBeAttributedWithConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns(c.Anchors, SpecSlot.AttributeName)) yield return pattern;
                break;

            // The hierarchy verbs' string anchors, on the same item-15 terms under their own labels, so the
            // error names which kind of anchor was left blank ("Blank interface name on 'rule/id'.").
            case MustImplementConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns([c.Anchor], SpecSlot.InterfaceName)) yield return pattern;
                break;
            case MustNotImplementConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns(c.Anchors, SpecSlot.InterfaceName)) yield return pattern;
                break;
            case MustDeriveFromConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns([c.Anchor], SpecSlot.BaseTypeName)) yield return pattern;
                break;
            case MustNotDeriveFromConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns(c.Anchors, SpecSlot.BaseTypeName)) yield return pattern;
                break;

            // The member attribute verbs' string anchors, on the same terms and under the same label — blank
            // is the whole of a definition FQN's well-formedness on either side of the axis (item 15).
            case MemberMustBeAttributedWithConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns([c.Anchor], SpecSlot.AttributeName)) yield return pattern;
                break;
            case MemberMustNotBeAttributedWithConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns(c.Anchors, SpecSlot.AttributeName)) yield return pattern;
                break;

            // The parameter verb's string anchor, under its own label — blankness is the whole of its
            // validation, the closed-generic refusal (item 20) being a typeof-arm fact.
            case MemberMustAcceptParameterConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns([c.Anchor], SpecSlot.ParameterTypeName)) yield return pattern;
                break;

            // GRAMMAR §8 items 25–26: the counterpart template, blank or placeholder-free, both decided by its slot.
            case MustHaveExactlyOneCounterpartConstraint c:
                yield return (c.Template, SpecSlot.CounterpartTemplate);
                break;

            // GRAMMAR §8 item 24: MustOnlyTarget's monikers, where a blank one would narrow the allow-list silently,
            // and MustTarget's, where it would red every evaluated project.
            case MustOnlyTargetConstraint c:
                foreach (string framework in c.Frameworks) yield return (framework, SpecSlot.TargetFramework);
                break;
            case MustTargetConstraint c:
                yield return (c.Framework, SpecSlot.TargetFramework);
                break;

            // The package verbs' globs: a blank one matches no package, which narrows an allow-list and empties a ban.
            case MustOnlyReferencePackagesConstraint c:
                foreach (string pattern in c.Patterns) yield return (pattern, SpecSlot.PackagePattern);
                break;
            case MustNotReferencePackagesConstraint c:
                foreach (string pattern in c.Patterns) yield return (pattern, SpecSlot.PackagePattern);
                break;
        }

        // The subject selection tree (for a member constraint this is the underlying type selection,
        // Subject => MemberSubject.Source) and the dependency-verb operands. A project constraint has no type
        // selection here; its name operands ride CheckProjectPatterns (item 23).
        if (constraint.Subject is { } subject)
            foreach ((string, SpecSlot) pattern in SelectionPatterns(subject))
                yield return pattern;

        foreach (Selection operand in constraint.Operands)
        foreach ((string, SpecSlot) pattern in SelectionPatterns(operand))
            yield return pattern;

        // The member subject's own adjectives (name/affix) — off the type-side selection walk, reached
        // like CheckMemberReturning reaches the member .Returning anchor.
        if (constraint is MemberConstraint memberConstraint)
            foreach (MemberAdjective adjective in memberConstraint.MemberSubject.Adjectives)
            foreach ((string, SpecSlot) pattern in MemberAdjectivePatterns(adjective))
                yield return pattern;
    }

    // The string arm of an anchor list, under one slot: an anchor spelled as a definition FQN yields its
    // name for the item-15 blank check, and a typeof anchor yields nothing. One projection for both anchor
    // shapes — the single-anchor verbs pass a one-element list — so every arm above stays one line.
    private static IEnumerable<(string Value, SpecSlot Slot)> AnchorNamePatterns(IReadOnlyList<TypeAnchor> anchors, SpecSlot slot)
    {
        foreach (TypeAnchor anchor in anchors)
            if (anchor.DefinitionFullName is { } name)
                yield return (name, slot);
    }

    private static IEnumerable<(string Value, SpecSlot Slot)> SelectionPatterns(Selection selection)
    {
        // A NamespaceNoun carries a glob and a ProjectNoun a name, both checked here — this one arm covers
        // every position a noun can stand in (subject, operand, Except payload, quarantined scope), because
        // each position reaches this walk. A LayerNoun's globs are validated once in ValidateLayers (their
        // use-independent home), so they are not re-checked here. A UnionSelection has no single noun, so
        // its operands answer for it — and either way the adjective loop below runs, because a union
        // carries adjectives of its own (AnyOf(a, b).InNamespace("") must reach the blank-pattern check).
        if (selection is UnionSelection union)
            foreach (Selection member in union.Parts)
            foreach ((string, SpecSlot) pattern in SelectionPatterns(member))
                yield return pattern;
        else if (selection.Noun is NamespaceNoun ns) yield return (ns.Glob, SpecSlot.NamespacePattern);
        else if (selection.Noun is ProjectNoun project) yield return (project.Name, SpecSlot.ProjectName);

        foreach (SelectionAdjective adjective in selection.Adjectives)
            switch (adjective)
            {
                case InNamespaceAdjective a:
                    yield return (a.Glob, SpecSlot.NamespacePattern);
                    break;
                case WithNameMatchingAdjective a:
                    yield return (a.Glob, SpecSlot.NamePattern);
                    break;
                case NamedAdjective a:
                    foreach (string name in a.Names) yield return (name, SpecSlot.ExactTypeName);

                    break;
                case WithSuffixAdjective a:
                    yield return (a.Suffix, SpecSlot.Suffix);
                    break;
                case WithPrefixAdjective a:
                    yield return (a.Prefix, SpecSlot.Prefix);
                    break;
                case AttributedWithAdjective { Anchor.DefinitionFullName: { } attributeName }:
                    // The adjective's string anchor, on the same terms as the two verbs' (item 15).
                    yield return (attributeName, SpecSlot.AttributeName);
                    break;
                case ImplementingAdjective { Anchor.DefinitionFullName: { } interfaceName }:
                    // The hierarchy adjectives' string anchors, likewise. Blankness is checked on an
                    // adjective even though item 21's category rule deliberately is not: a blank name is a
                    // spec-side slip either way, while a wrong CATEGORY on an adjective empties the subject
                    // and the fail-on-empty gate reds it loudly (see CheckHierarchyAnchors).
                    yield return (interfaceName, SpecSlot.InterfaceName);
                    break;
                case DerivedFromAdjective { Anchor.DefinitionFullName: { } baseTypeName }:
                    yield return (baseTypeName, SpecSlot.BaseTypeName);
                    break;
                case ExceptAdjective a:
                    foreach ((string, SpecSlot) pattern in SelectionPatterns(a.Payload)) yield return pattern;
                    break;
            }
    }

    private static IEnumerable<(string Value, SpecSlot Slot)> MemberAdjectivePatterns(MemberAdjective adjective)
    {
        switch (adjective)
        {
            case MemberWithNameMatchingAdjective a:
                yield return (a.Glob, SpecSlot.MemberNamePattern);
                break;
            case MemberWithSuffixAdjective a:
                yield return (a.Suffix, SpecSlot.MemberSuffix);
                break;
            case MemberWithPrefixAdjective a:
                yield return (a.Prefix, SpecSlot.MemberPrefix);
                break;
            case MemberAttributedWithAdjective { Anchor.DefinitionFullName: { } attributeName }:
                // The member attribute adjective's string anchor (item 15). The category check deliberately
                // does not reach an adjective on either axis — see CheckHierarchyAnchors.
                yield return (attributeName, SpecSlot.AttributeName);
                break;
            case ReturningAdjective a:
                // The return-type adjective's string anchors, on the same terms under their own label. The
                // list form goes through the shared projection, so every anchor is reached.
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns(a.Anchors, SpecSlot.ReturnTypeName)) yield return pattern;

                break;
        }
    }
}
