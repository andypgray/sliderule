using SlideRule.Internal;
using SlideRule.Model;
using SlideRule.Prose;
using Code = SlideRule.Validation.SpecValidationErrorCode;

namespace SlideRule.Validation;

/// <summary>
///     Runs the whole GRAMMAR §8 catalog and collects every error in one pass.
/// </summary>
/// <remarks>
///     ID checks run over the post-desugar ID set; authored-field checks run over the original
///     registrations, keyed to the rule or scope the author wrote. A rule, scope or member error carries its
///     anchor's spec-source location (GRAMMAR §8); a layer error is spec-wide and carries none.
/// </remarks>
internal static class SpecValidator
{
    // GRAMMAR §8 item 36: what each call a computed list can leave empty does with no operand, and what to pass
    // instead. The call is spelled as the author wrote it, the effect completes "so it", and the cure is the
    // instruction after the semicolon.
    private static readonly (string Call, string Effect, string Cure) EmptyUnion =
        ("arch.AnyOf", "selects nothing", "pass at least one operand");

    private static readonly (string Call, string Effect, string Cure) EmptyProjectNamed =
        ("Named", "selects no project", "pass at least one project name");

    private static readonly (string Call, string Effect, string Cure) EmptyProjectMatching =
        ("Matching", "selects no project", "pass at least one project name glob");

    private static readonly (string Call, string Effect, string Cure) EmptyMemberNamed =
        ("Named", "selects no member", "pass at least one member name");

    private static readonly (string Call, string Effect, string Cure) EmptyMemberExcept =
        ("Except", "removes nothing", "pass at least one member selection");

    private static readonly (string Call, string Effect, string Cure) EmptyMustNotUse =
        ("MustNotUse", "bans nothing", "pass at least one member");

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

            var target = new ErrorTarget(null, null, $"layer '{layer.Name}'");

            // Blank and duplicate in one pass, so the two report in authoring order rather than every blank
            // before every duplicate.
            if (!CheckLiteral(layer.Name, SpecSlot.LayerName, target, errors)
                && duplicates.TryGetValue(index, out CollisionVerdict duplicate))
                errors.Add(new SpecValidationError(duplicate.Code, null, duplicate.Sentence));

            // A layer's definition is validated here, its use-independent home, so a bad one is caught whether
            // or not the layer is ever used as a subject (§8 items 10, 15–16, 19).
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

            // A purpose is prose like any other (§8 items 5–6).
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

        // The extends-scope and duplicate verdicts (§8 items 1 and 7). The key is already the ID the error is
        // addressed to, desugared children included.
        foreach (CollisionVerdict collision in SpecIdCollisions.Decide(declarations))
        {
            Registration site = registrations[collision.Tags[0]];
            errors.Add(new SpecValidationError(collision.Code, collision.Key, collision.Sentence, site.Location));
        }
    }

    private static void ValidateRule(RuleRegistration rule, Arch arch, List<SpecValidationError> errors)
    {
        if (rule.Posture is null)
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
        CheckMemberExceptKinds(rule, errors);
        CheckMemberAcceptParameter(rule, errors);
        CheckHierarchyAnchors(rule, errors);
        CheckPatterns(RulePatterns(rule), target, errors);
        CheckLifetimes(selections, target, errors);
        CheckFamilies(RuleFamilyPositions(rule), target, errors);
        CheckEachOtherSubject(rule, errors);
        CheckCircularReferencesSubject(rule, errors);
        CheckEmptyOperandLists(RuleEmptyOperandLists(rule, selections), target, errors);
    }

    private static void ValidateScope(ScopeRegistration scope, Arch arch, List<SpecValidationError> errors)
    {
        if (scope.Posture is null)
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
        CheckEmptyOperandLists(EmptyUnions(expanded), target, errors);
    }

    // GRAMMAR §8 items 15–16. Every glob, affix and string anchor a rule carries, each already tagged with
    // the slot that names it in the error and owns what a literal written there can be wrong about.
    private static void CheckPatterns(
        IEnumerable<(string Value, SpecSlot Slot)> patterns, ErrorTarget target, List<SpecValidationError> errors)
    {
        foreach ((string value, SpecSlot slot) in patterns) CheckLiteral(value, slot, target, errors);
    }

    // Where an error lands and what its sentence calls the offender: a rule or scope error quotes its ID and
    // carries the anchor's location, and a layer error has neither and names the layer outright. One value, so
    // the three cannot be threaded apart or transposed.
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
            if (selection.FamilyNounOrNull() is null) continue;

            errors.Add(new SpecValidationError(Code.FamilyMisplaced, target.Id,
                $"A family (`arch.Each`) used as {position} by {target.Subject}; "
                + "a family may stand only as a rule subject.", target.Location));
        }
    }

    // GRAMMAR §8 item 36. The params forms cannot be called without a first operand, so an empty list reaches
    // the model only through a sequence overload, and it would select, remove or ban nothing while reading like a
    // rule. One code and one sentence shape for every call, naming the call and what an empty one does, because
    // "empty" alone does not say why the spec is wrong.
    private static void CheckEmptyOperandLists(
        IEnumerable<(string Call, string Effect, string Cure)> empties, ErrorTarget target,
        List<SpecValidationError> errors)
    {
        foreach ((string call, string effect, string cure) in empties)
            errors.Add(new SpecValidationError(Code.EmptyOperandList, target.Id,
                $"`{call}` on {target.Subject} was given an empty list, so it {effect}; {cure}.", target.Location));
    }

    private static IEnumerable<(string Call, string Effect, string Cure)> RuleEmptyOperandLists(
        RuleRegistration rule, IEnumerable<Selection> selections)
    {
        foreach ((string, string, string) empty in EmptyUnions(selections)) yield return empty;

        if (rule.Constraint is not { } constraint) yield break;

        foreach (ProjectSelection selection in constraint.ProjectSelections())
        foreach (ProjectAdjective adjective in selection.Adjectives)
            switch (adjective)
            {
                case ProjectNamedAdjective { Names.Count: 0 }:
                    yield return EmptyProjectNamed;
                    break;
                case ProjectMatchingAdjective { Globs.Count: 0 }:
                    yield return EmptyProjectMatching;
                    break;
            }

        foreach (MemberSelection selection in constraint.MemberSelections())
        foreach (MemberAdjective adjective in selection.Adjectives)
            switch (adjective)
            {
                case MemberNamedAdjective { Names.Count: 0 }:
                    yield return EmptyMemberNamed;
                    break;
                case MemberExceptAdjective { Payloads.Count: 0 }:
                    yield return EmptyMemberExcept;
                    break;
            }

        if (constraint is MustNotUseConstraint { MemberOperands.Count: 0 }) yield return EmptyMustNotUse;
    }

    // Over a walk that has already expanded every union, so a nested empty one is found where it stands. It stays
    // a leaf of the walk because UnionSelection.Create flattens only a non-empty inner union.
    private static IEnumerable<(string Call, string Effect, string Cure)> EmptyUnions(IEnumerable<Selection> expanded)
    {
        return expanded
            .OfType<UnionSelection>()
            .Where(union => union.Parts.Count == 0)
            .Select(_ => EmptyUnion);
    }

    // GRAMMAR §8 item 28: the cross-cell ban's targets are the subject's own cells, so a subject with no
    // cells names nothing to forbid. The message names the verb the author meant, because over a plain
    // selection there is one and it is not a rewrite of the rule.
    private static void CheckEachOtherSubject(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MustNotReferenceEachOtherConstraint constraint) return;

        if (constraint.Subject.FamilyNounOrNull() is not null) return;

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

        if (constraint.Subject.FamilyNounOrNull() is not { } family)
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
    // Subject, its projection's source, is a subject too, so a family there is legal.
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

        // A member Except payload's source consumes a set like any other payload, so a family there is refused
        // as one; only the subject's own source is a subject.
        if (constraint is not MemberConstraint member) yield break;

        foreach (MemberSelection payload in member.MemberSubject.ExceptPayloads())
        foreach ((Selection, string) position in FamilyPositions(payload.Source, FamilyPosition.ExceptPayload))
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
    // mistake however many of its parts carry it. Returns whether anything was reported, which is what lets a
    // caller skip the per-item checks a foreign owner makes meaningless.
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

    // GRAMMAR §8 item 22: the project-stratum sibling of CheckForeign, needed because the type-side walk reaches
    // nothing on a project constraint. It includes the Except payloads, where a foreign project selection can
    // hide inside a local subject.
    private static void CheckForeignProjects(
        RuleRegistration rule, ErrorTarget target, Arch arch, List<SpecValidationError> errors)
    {
        if (rule.Constraint is null) return;

        IEnumerable<ProjectSelection> selections = rule.Constraint.ProjectSelections();
        IEnumerable<(Arch Owner, SpecSourceLocation? Location)> candidates =
            selections.Select(selection => (selection.Owner, rule.Location));

        ReportFirstForeign(
            candidates, arch, Code.ForeignProjectSelection, "A project selection", target, errors);
    }

    // GRAMMAR §8 item 23: the blank check over a project subject's own .Named names and .Matching globs, under
    // the project stratum's own code (SpecSlot.ProjectNameOperand says why).
    private static void CheckProjectPatterns(
        RuleRegistration rule, ErrorTarget target, List<SpecValidationError> errors)
    {
        if (rule.Constraint is null) return;

        foreach (ProjectSelection selection in rule.Constraint.ProjectSelections())
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

    // GRAMMAR §8 items 11–13: the member-access verb's operands. A foreign member is reported once per rule
    // and stops the pass; a member error renders at its own arch.Member(...) call site, or at the rule's
    // anchor for a verb-minted member, which carries no location.
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

    // GRAMMAR §8 item 14, through CheckClosedGenericAnchors: a member `.Returning` anchor is definition-level, so
    // a closed-generic one (typeof(Task<int>)) is refused with guidance to the open definition (typeof(Task<>)).
    private static void CheckMemberReturning(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is null) return;

        foreach (MemberSelection selection in rule.Constraint.MemberSelections())
        foreach (MemberAdjective adjective in selection.Adjectives)
            if (adjective is ReturningAdjective returning)
                CheckClosedGenericAnchors(rule, errors, returning.Anchors, Code.MemberReturningClosedGeneric, ".Returning");
    }

    // GRAMMAR §8 item 34, rendered at the rule's location like the closed-generic refusal. A .Members selection
    // on either side holds every kind and clashes with none.
    private static void CheckMemberExceptKinds(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is null) return;

        foreach (MemberSelection host in rule.Constraint.MemberSelections())
        foreach (MemberAdjective adjective in host.Adjectives)
        {
            if (adjective is not MemberExceptAdjective except) continue;

            foreach (MemberSelection payload in except.Payloads)
            {
                if (host.Kind == MemberKindFilter.Any || payload.Kind == MemberKindFilter.Any) continue;
                if (host.Kind == payload.Kind) continue;

                string taken = ProseFormat.MemberKindPlural(payload.Kind);
                string from = ProseFormat.MemberKindPlural(host.Kind);
                errors.Add(new SpecValidationError(Code.MemberExceptKindDisjoint, rule.Id,
                    $"`Except` on '{rule.Id}' takes {taken} from {from}, so it removes nothing: no member is both. "
                    + $"Project the payload with `.{ProseFormat.Capitalize(from)}` or `.Members`.", rule.Location));
            }
        }
    }

    // GRAMMAR §8 item 20, through CheckClosedGenericAnchors: the MustAcceptParameter sibling of the item-14
    // .Returning refusal.
    private static void CheckMemberAcceptParameter(RuleRegistration rule, List<SpecValidationError> errors)
    {
        if (rule.Constraint is not MemberMustAcceptParameterConstraint accept) return;

        CheckClosedGenericAnchors(
            rule, errors, [accept.Anchor], Code.MemberAcceptParameterClosedGeneric, "MustAcceptParameter");
    }

    // The definition-level refusal items 14 and 20 share, with the caller's verb in the steer and its own
    // code in the catalog. The anchor is the consuming rule's statement, so the error renders at the rule's
    // location; a string anchor carries no reflected type, so TypedAnchors drops it before anything is judged.
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

    // GRAMMAR §8 item 21, both polarities; Code.HierarchyAnchorWrongCategory carries the category rule per
    // verb. String anchors are filtered out (TypedAnchors) in every family, so a wrong string is loud on a
    // positive (always red) and silent on a negative.
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

            // The member attribute verbs (GRAMMAR §5.7) share the type side's rule and message. Adjectives stay
            // unchecked on both sides: a wrong-category adjective empties the subject, which the fail-on-empty
            // gate reds loudly, while the always-passing MustNot verb is the silent slip this item exists to catch.
            case MemberMustBeAttributedWithConstraint c:
                CheckAttributeAnchors(rule, errors, [c.Anchor], "MustBeAttributedWith");
                break;
            case MemberMustNotBeAttributedWithConstraint c:
                CheckAttributeAnchors(rule, errors, c.Anchors, "MustNotBeAttributedWith");
                break;
        }
    }

    // The attribute-anchor half of item 21: one predicate and one message template, the caller's verb named in
    // the steer.
    private static void CheckAttributeAnchors(
        RuleRegistration rule, List<SpecValidationError> errors, IReadOnlyList<TypeAnchor> anchors, string verbName)
    {
        CheckAnchorCategory(rule, errors, TypedAnchors(anchors), t => !t.IsSubclassOf(typeof(Attribute)),
            t => $"'{SafeFullDisplay(t)}' does not derive from System.Attribute; {verbName} requires an attribute anchor");
    }

    private static void CheckAnchorCategory(
        RuleRegistration rule, List<SpecValidationError> errors, IReadOnlyList<Type> anchors,
        Func<Type, bool> invalid, Func<Type, string> message)
    {
        foreach (Type anchor in anchors)
            if (invalid(anchor))
                errors.Add(new SpecValidationError(Code.HierarchyAnchorWrongCategory, rule.Id,
                    $"{message(anchor)} (used by '{rule.Id}').", rule.Location));
    }

    // The reflected arm of an anchor list, the only anchors items 14, 20 and 21 can judge. A string anchor names a
    // definition FQN and nothing more, so it is dropped here: the blank-name check (item 15, via RulePatterns) is
    // the whole of what it is validated for, and a constructed spelling is a legal name that matches nothing.
    private static IReadOnlyList<Type> TypedAnchors(IReadOnlyList<TypeAnchor> anchors)
    {
        return anchors.Where(anchor => anchor.Type is not null).Select(anchor => anchor.Type!).ToList();
    }

    // GRAMMAR §8 item 19: a Lifetime outside the defined set (a cast such as (Lifetime)7) names no lifetime.
    // Walked through the same expansion the foreign-Arch and pattern walks take, so a noun cannot be checked in
    // one position and unchecked in another; the build throws before membership resolution sees the value.
    private static void CheckLifetimes(
        IEnumerable<Selection> selections, ErrorTarget target, List<SpecValidationError> errors)
    {
        foreach (Selection selection in selections)
            if (selection.NounOrNull() is RegisteredNoun { Lifetime: { } lifetime }
                && !Enum.IsDefined(typeof(Lifetime), lifetime))
                errors.Add(new SpecValidationError(Code.UndefinedLifetime, target.Id,
                    $"'(Lifetime){(int)lifetime}' is not a defined Lifetime — " +
                    $"use Lifetime.Singleton, Lifetime.Scoped, or Lifetime.Transient (used by {target.Subject}).",
                    target.Location));
    }

    private static void CheckMember(Member member, ErrorTarget rule, List<SpecValidationError> errors)
    {
        // The ID stays the rule's wherever the location comes from: a member is never what a spec error is
        // addressed to.
        var target = new ErrorTarget(rule.Id, member.Location ?? rule.Location);

        // A poisoned member (GRAMMAR §8, item 12's expression sibling) is reported before its throwing anchor
        // accessors are touched.
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
        if (declaringBase is not null)
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

        for (Type? baseType = anchor.BaseType; baseType is not null; baseType = baseType.BaseType)
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

        // A path, walked as prose: a blank one resolves to the solution directory rather than failing, so check
        // reads no baseline and the rule reds with nothing said about why, and `baseline init` tries to write
        // the directory itself.
        foreach (string baseline in rule.Baselines) yield return (SpecSlot.Baseline, baseline);

        if (rule.Constraint is not null)
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

        // The member escape hatches (GRAMMAR §4.6, §8 item 5).
        if (constraint is MemberMustConstraint memberMust) yield return (SpecSlot.Description, memberMust.Description);

        if (constraint is MemberConstraint memberConstraint)
        {
            foreach ((SpecSlot, string?) prose in MemberWhereProse(memberConstraint.MemberSubject))
                yield return prose;

            // Each member Except payload is a member selection in its own right: its source's prose and its own
            // member Where descriptions are walked as the subject's are.
            foreach (MemberSelection payload in memberConstraint.MemberSubject.ExceptPayloads())
            {
                foreach ((SpecSlot, string?) prose in SelectionProse(payload.Source))
                    yield return prose;

                foreach ((SpecSlot, string?) prose in MemberWhereProse(payload))
                    yield return prose;
            }
        }

        // The project escape hatches (GRAMMAR §4.10, §8 item 5), Except payloads included.
        if (constraint is ProjectMustConstraint projectMust) yield return (SpecSlot.Description, projectMust.Description);

        foreach (ProjectSelection selection in constraint.ProjectSelections())
        foreach (ProjectAdjective adjective in selection.Adjectives)
            if (adjective is ProjectWhereAdjective projectWhere)
                yield return (SpecSlot.Description, projectWhere.Description);

        // For a member constraint this is the projection's source, so a type-side Where or Except before the
        // projection is walked too; a project constraint's Subject is null, and the loop above is its walk.
        if (constraint.Subject is { } subject)
            foreach ((SpecSlot, string?) prose in SelectionProse(subject))
                yield return prose;

        foreach (Selection operand in constraint.Operands)
        foreach ((SpecSlot, string?) prose in SelectionProse(operand))
            yield return prose;
    }

    // A member selection's own Where descriptions, in authoring order: what the subject and each Except payload
    // contribute beyond their sources.
    private static IEnumerable<(SpecSlot Slot, string? Value)> MemberWhereProse(MemberSelection selection)
    {
        foreach (MemberAdjective adjective in selection.Adjectives)
            if (adjective is MemberWhereAdjective where)
                yield return (SpecSlot.Description, where.Description);
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
        IEnumerable<Selection> constraintSelections = rule.Constraint is null
            ? Enumerable.Empty<Selection>()
            : rule.Constraint.Selections();
        IEnumerable<Selection> exemplarSelections = rule.Exemplars.SelectMany(SelectionWalk.ExpandSelection);
        return constraintSelections.Concat(exemplarSelections);
    }

    // The glob, affix and string-anchor walk (GRAMMAR §8 items 15–16), parallel to the prose and selection walks.
    private static IEnumerable<(string Value, SpecSlot Slot)> RulePatterns(RuleRegistration rule)
    {
        IEnumerable<(string, SpecSlot)> constraintPatterns = rule.Constraint is null
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
            // name would otherwise mint an anchor matching nothing, silently.
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

            // The member attribute verbs' string anchors, on the same terms and under the same label.
            case MemberMustBeAttributedWithConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns([c.Anchor], SpecSlot.AttributeName)) yield return pattern;
                break;
            case MemberMustNotBeAttributedWithConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns(c.Anchors, SpecSlot.AttributeName)) yield return pattern;
                break;

            // The parameter verb's string anchor, under its own label.
            case MemberMustAcceptParameterConstraint c:
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns([c.Anchor], SpecSlot.ParameterTypeName)) yield return pattern;
                break;

            // GRAMMAR §8 items 25–26: the counterpart template, blank or placeholder-free, both decided by its slot.
            case MustHaveExactlyOneCounterpartConstraint c:
                yield return (c.Template, SpecSlot.CounterpartTemplate);
                break;

            // GRAMMAR §8 item 24: MustOnlyTarget's and MustTarget's monikers.
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

            // The property verb's two strings, either of which blank asks for a property no project can declare.
            case MustSetPropertyConstraint c:
                yield return (c.Name, SpecSlot.PropertyName);
                yield return (c.Value, SpecSlot.PropertyValue);
                break;
        }

        // The subject tree and the dependency-verb operands; a project constraint's name operands ride
        // CheckProjectPatterns (item 23).
        if (constraint.Subject is { } subject)
            foreach ((string, SpecSlot) pattern in SelectionPatterns(subject))
                yield return pattern;

        foreach (Selection operand in constraint.Operands)
        foreach ((string, SpecSlot) pattern in SelectionPatterns(operand))
            yield return pattern;

        // The member subject's own adjectives, off the type-side selection walk.
        if (constraint is not MemberConstraint memberConstraint) yield break;

        foreach ((string, SpecSlot) pattern in MemberSelectionPatterns(memberConstraint.MemberSubject))
            yield return pattern;

        // Then each member Except payload, its source's patterns before its own, as the subject reads.
        foreach (MemberSelection payload in memberConstraint.MemberSubject.ExceptPayloads())
        {
            foreach ((string, SpecSlot) pattern in SelectionPatterns(payload.Source))
                yield return pattern;

            foreach ((string, SpecSlot) pattern in MemberSelectionPatterns(payload))
                yield return pattern;
        }
    }

    // The string arm of an anchor list, under one slot: an anchor spelled as a definition FQN yields its
    // name for the item-15 blank check, and a typeof anchor yields nothing.
    private static IEnumerable<(string Value, SpecSlot Slot)> AnchorNamePatterns(IReadOnlyList<TypeAnchor> anchors, SpecSlot slot)
    {
        foreach (TypeAnchor anchor in anchors)
            if (anchor.DefinitionFullName is { } name)
                yield return (name, slot);
    }

    private static IEnumerable<(string Value, SpecSlot Slot)> SelectionPatterns(Selection selection)
    {
        // Every position a noun can stand in reaches this walk, though a layer's globs are validated once, in
        // ValidateLayers, and not re-checked here. A union's operands answer for its noun, and the adjective loop
        // runs either way: AnyOf(a, b).InNamespace("") must reach the blank-pattern check.
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
                case UnderAdjective a:
                    foreach (string glob in a.Globs) yield return (glob, SpecSlot.PathGlob);

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
                    // The hierarchy adjectives' string anchors, likewise: blankness is checked on an adjective,
                    // though item 21's category rule is not (see CheckHierarchyAnchors).
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

    private static IEnumerable<(string Value, SpecSlot Slot)> MemberSelectionPatterns(MemberSelection selection)
    {
        return selection.Adjectives.SelectMany(MemberAdjectivePatterns);
    }

    private static IEnumerable<(string Value, SpecSlot Slot)> MemberAdjectivePatterns(MemberAdjective adjective)
    {
        switch (adjective)
        {
            case MemberWithNameMatchingAdjective a:
                yield return (a.Glob, SpecSlot.MemberNamePattern);
                break;
            case MemberNamedAdjective a:
                foreach (string name in a.Names) yield return (name, SpecSlot.ExactMemberName);

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
                // The return-type adjective's string anchors, on the same terms under their own label.
                foreach ((string, SpecSlot) pattern in AnchorNamePatterns(a.Anchors, SpecSlot.ReturnTypeName)) yield return pattern;

                break;
        }
    }
}
