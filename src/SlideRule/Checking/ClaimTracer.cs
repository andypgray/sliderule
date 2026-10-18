using SlideRule.Codebase;
using SlideRule.Fluent;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Model;
using SlideRule.Prose;

namespace SlideRule.Checking;

/// <summary>
///     Traces why a selection names one type, or why it does not: the steps a selection's noun, its parts
///     and its adjectives took over that type, each naming the fact that decided it.
/// </summary>
/// <remarks>
///     Every membership question goes to <see cref="SelectionEvaluator" />, one type at a time
///     (<see cref="SelectionEvaluator.NounAdmits" />, <see cref="SelectionEvaluator.AdjectiveKeeps" />), so the
///     trace and a check cannot disagree about what a noun names or an adjective keeps. The one thing restated
///     here is <see cref="SelectionEvaluator.Evaluate" />'s compound fold — the parts unite, then the compound's
///     own adjectives narrow — and the parity test over the fixture specs holds the two folds together.
///     <para>
///         The walk stops at the first adjective that drops the type, because a narrowing never runs a later
///         adjective on a type an earlier one dropped, and a <c>Where</c> predicate is user code that must not
///         run where the checker would not run it.
///     </para>
/// </remarks>
internal sealed class ClaimTracer(CodebaseModel model, SolutionRoot? root = null)
{
    private readonly SelectionEvaluator _evaluator = new(model, root);

    /// <summary>
    ///     A subject's verdict on <paramref name="type" />, with a failed evaluation caught as its own verdict.
    /// </summary>
    internal ClaimTrace Trace(Selection selection, TypeNode type)
    {
        return Guarded(() => Trace(selection, SelectionPosition.Subject, type));
    }

    /// <summary>
    ///     A declared layer's verdict on <paramref name="type" />: its definition's trace, or the glob its
    ///     namespace matched.
    /// </summary>
    internal ClaimTrace Trace(LayerDefinition layer, TypeNode type)
    {
        return Guarded(() =>
        {
            if (layer.Definition is { } definition) return Trace(definition, SelectionPosition.Subject, type);

            return _evaluator.NounAdmits(layer.Noun, SelectionPosition.Subject, type)
                ? new ClaimTrace(ClaimVerdict.Claimed, [GlobStep(layer.Globs, type)])
                : ClaimTrace.Unreached;
        });
    }

    // The same containment the checker applies per rule: a selection that cannot be evaluated is that one
    // claimant's failure, and every other claimant still answers.
    private static ClaimTrace Guarded(Func<ClaimTrace> trace)
    {
        try
        {
            return trace();
        }
        catch (RuleEvaluationException ex)
        {
            return new ClaimTrace(ClaimVerdict.Failed, [new ClaimStep($"could not be evaluated: {ex.Message}")]);
        }
    }

    private ClaimTrace Trace(Selection selection, SelectionPosition position, TypeNode type)
    {
        List<ClaimStep>? steps = _evaluator.Composite(selection) is { } parts
            ? PartSteps(selection, parts, position, type)
            : NounSteps(selection, position, type);
        if (steps is null) return ClaimTrace.Unreached;

        foreach (SelectionAdjective adjective in selection.Adjectives)
        {
            if (!_evaluator.AdjectiveKeeps(adjective, type))
            {
                steps.Add(Dropped(adjective, type));
                return new ClaimTrace(adjective is ExceptAdjective ? ClaimVerdict.Excepted : ClaimVerdict.Narrowed, steps);
            }

            steps.Add(Kept(adjective, type));
        }

        return new ClaimTrace(ClaimVerdict.Claimed, steps);
    }

    // The compound half of the fold, or null when no part names the type. A layer is transparent to its
    // definition, so its one part traces under a step naming the layer. A bare part names itself in its own
    // first step and is listed inline; beside other parts, a part that carries adjectives, or is itself a union,
    // traces under a step quoting it, so its adjectives read as its own. A lone part, such as a one-operand
    // Except payload, is already quoted by the step above it.
    private List<ClaimStep>? PartSteps(
        Selection selection, IReadOnlyList<Selection> parts, SelectionPosition position, TypeNode type)
    {
        if (SelectionWalk.NounOf(selection) is LayerNoun layer)
        {
            ClaimTrace definition = Trace(parts[0], position, type);
            return definition.Verdict == ClaimVerdict.Claimed ? [new ClaimStep(LayerStep(layer.Name), definition.Steps)] : null;
        }

        var steps = new List<ClaimStep>();
        foreach (Selection part in parts)
        {
            ClaimTrace trace = Trace(part, position, type);
            if (trace.Verdict != ClaimVerdict.Claimed) continue;

            if (parts.Count > 1 && (part is UnionSelection || part.Adjectives.Count > 0))
                steps.Add(new ClaimStep($"through {SentenceRenderer.Reference(part)}", trace.Steps));
            else
                steps.AddRange(trace.Steps);
        }

        return steps.Count > 0 ? steps : null;
    }

    // arch.Types admits every type the solution declares, so under adjectives it decides nothing worth a step and
    // the adjectives carry the trace; bare, it is the whole reason.
    private List<ClaimStep>? NounSteps(Selection selection, SelectionPosition position, TypeNode type)
    {
        SelectionNoun noun = selection.Noun;
        if (!_evaluator.NounAdmits(noun, position, type)) return null;
        if (noun is TypesNoun && selection.Adjectives.Count > 0) return [];

        ClaimStep step = noun switch
        {
            TypesNoun => new ClaimStep("every type the solution declares"),
            LayerNoun layer => new ClaimStep(LayerStep(layer.Name), [GlobStep(layer.Globs, type)]),
            NamespaceNoun ns => new ClaimStep($"{NamespaceDisplay(type)} matches {ProseFormat.Backtick(ns.Glob)}"),
            ProjectNoun project => new ClaimStep($"declared by project {ProseFormat.Backtick(project.Name)}"),
            TypeNoun named => new ClaimStep($"it is the named type {named.ReferenceFragment}"),
            RegisteredNoun registered => RegistrationStep(registered.Lifetime, type),
            _ => new ClaimStep(noun.ReferenceFragment)
        };

        return [step];
    }

    private static string LayerStep(string name)
    {
        return $"in the {name} layer";
    }

    // The first glob that matches, in declaration order: the noun admitted the type, so one does.
    private static ClaimStep GlobStep(IReadOnlyList<string> globs, TypeNode type)
    {
        string glob = globs.First(candidate => new NamespacePattern(candidate).Matches(type.Namespace));
        return new ClaimStep($"{NamespaceDisplay(type)} matches {ProseFormat.Backtick(glob)}");
    }

    // The registration that put the type in the noun's set, and the role it plays there. The noun admitted the
    // type, so one registration at the noun's lifetime names it as its service or its implementation.
    private ClaimStep RegistrationStep(Lifetime? lifetime, TypeNode type)
    {
        foreach (ServiceRegistration registration in model.ServiceRegistrations)
        {
            if (lifetime is { } wanted && registration.Lifetime != wanted) continue;

            string? role = registration.ServiceFullName == type.FullName ? "service"
                : registration.ImplementationFullName == type.FullName ? "implementation"
                : null;
            if (role is null) continue;

            string kind = registration.Lifetime.ToString().ToLowerInvariant();
            return new ClaimStep($"registered as the {role} of a {kind} registration");
        }

        return new ClaimStep(ProseFormat.RegisteredFragment(lifetime));
    }

    private ClaimStep Kept(SelectionAdjective adjective, TypeNode type)
    {
        return adjective switch
        {
            InNamespaceAdjective inNamespace =>
                new ClaimStep($"{NamespaceDisplay(type)} matches {ProseFormat.Backtick(inNamespace.Glob)}"),
            OfKindAdjective => new ClaimStep($"it is {KindPhrase(type.Kind)}"),
            WithSuffixAdjective suffix =>
                new ClaimStep($"its name {ProseFormat.Backtick(type.Name)} ends with {ProseFormat.Backtick(suffix.Suffix)}"),
            WithPrefixAdjective prefix =>
                new ClaimStep($"its name {ProseFormat.Backtick(type.Name)} starts with {ProseFormat.Backtick(prefix.Prefix)}"),
            WithNameMatchingAdjective matching =>
                new ClaimStep($"its name {ProseFormat.Backtick(type.Name)} matches {ProseFormat.Backtick(matching.Glob)}"),
            NamedAdjective => new ClaimStep($"it is named {ProseFormat.Backtick(type.Name)}"),
            ImplementingAdjective implementing =>
                new ClaimStep($"it implements {ProseFormat.Backtick(ProseFormat.AnchorName(implementing.Anchor))}"),
            DerivedFromAdjective derivedFrom =>
                new ClaimStep($"it derives from {ProseFormat.Backtick(ProseFormat.AnchorName(derivedFrom.Anchor))}"),
            AttributedWithAdjective attributedWith =>
                new ClaimStep($"it is attributed with {ProseFormat.Backtick(ProseFormat.AttributeName(attributedWith.Anchor))}"),
            ExceptAdjective except => new ClaimStep($"outside the except: {SentenceRenderer.Reference(except.Payload)}"),
            WhereAdjective where => new ClaimStep($"it passes the predicate \"{where.Description}\""),
            AuthoredAdjective => new ClaimStep("it is authored, not generated"),
            AccessibilityAdjective => new ClaimStep($"it is declared {AccessibilityWord(type.Accessibility)}"),
            UnderAdjective under => UnderStep(under, type),
            _ => new ClaimStep($"it passes \"{adjective.Fragment.Trim()}\"")
        };
    }

    // The first declaring file under one of the globs, in file order, with the glob it is under: the adjective
    // kept the type, so one pair exists.
    private ClaimStep UnderStep(UnderAdjective under, TypeNode type)
    {
        foreach (string path in type.FilePaths)
        {
            if (_evaluator.RelativeSegments(path) is not { } segments) continue;

            foreach (string glob in under.Globs)
                if (SelectionEvaluator.UnderAny([glob])(segments))
                    return new ClaimStep(
                        $"it is declared in {ProseFormat.Backtick(string.Join("/", segments))}, under {ProseFormat.Backtick(glob)}");
        }

        return new ClaimStep($"it passes \"{under.Fragment.Trim()}\"");
    }

    // Why no file put the type under the globs: it has none in the solution, or none of them is under any.
    private static ClaimStep NotUnderStep(UnderAdjective under, TypeNode type)
    {
        return type.FilePaths.Count == 0
            ? new ClaimStep("it has no source file in the solution")
            : new ClaimStep($"none of the files declaring it is under {ProseFormat.BacktickedList(under.Globs)}");
    }

    private ClaimStep Dropped(SelectionAdjective adjective, TypeNode type)
    {
        return adjective switch
        {
            InNamespaceAdjective inNamespace =>
                new ClaimStep($"{NamespaceDisplay(type)} does not match {ProseFormat.Backtick(inNamespace.Glob)}"),
            OfKindAdjective ofKind => new ClaimStep($"it is {KindPhrase(type.Kind)}, not {KindPhrase(ofKind.Kind)}"),
            WithSuffixAdjective suffix =>
                new ClaimStep($"its name {ProseFormat.Backtick(type.Name)} does not end with {ProseFormat.Backtick(suffix.Suffix)}"),
            WithPrefixAdjective prefix =>
                new ClaimStep($"its name {ProseFormat.Backtick(type.Name)} does not start with {ProseFormat.Backtick(prefix.Prefix)}"),
            WithNameMatchingAdjective matching =>
                new ClaimStep($"its name {ProseFormat.Backtick(type.Name)} does not match {ProseFormat.Backtick(matching.Glob)}"),
            NamedAdjective named =>
                new ClaimStep($"its name {ProseFormat.Backtick(type.Name)} is not {ProseFormat.BacktickedList(named.Names)}"),
            ImplementingAdjective implementing =>
                new ClaimStep($"it does not implement {ProseFormat.Backtick(ProseFormat.AnchorName(implementing.Anchor))}"),
            DerivedFromAdjective derivedFrom =>
                new ClaimStep($"it does not derive from {ProseFormat.Backtick(ProseFormat.AnchorName(derivedFrom.Anchor))}"),
            AttributedWithAdjective attributedWith =>
                new ClaimStep(
                    $"it is not attributed with {ProseFormat.Backtick(ProseFormat.AttributeName(attributedWith.Anchor))}"),
            // The removal is the surprise this trace exists to explain, so the payload's own trace says why
            // the type is in it — read in target position, where an Except payload is always resolved.
            ExceptAdjective except => new ClaimStep(
                $"removed by the except: {SentenceRenderer.Reference(except.Payload)}",
                Trace(except.Payload, SelectionPosition.Target, type).Steps),
            WhereAdjective where => new ClaimStep($"it fails the predicate \"{where.Description}\""),
            AuthoredAdjective => new ClaimStep("it is generated, not authored"),
            AccessibilityAdjective accessibility => new ClaimStep(
                $"it is declared {AccessibilityWord(type.Accessibility)}, not {AccessibilityWord(accessibility.Accessibility)}"),
            UnderAdjective under => NotUnderStep(under, type),
            _ => new ClaimStep($"it fails \"{adjective.Fragment.Trim()}\"")
        };
    }

    private static string NamespaceDisplay(TypeNode type)
    {
        return type.Namespace.Length == 0 ? "the global namespace" : $"namespace {ProseFormat.Backtick(type.Namespace)}";
    }

    private static string KindPhrase(TypeKind kind)
    {
        string word = kind.ToString().ToLowerInvariant();
        return kind is TypeKind.Interface or TypeKind.Enum ? $"an {word}" : $"a {word}";
    }

    private static string AccessibilityWord(Accessibility accessibility)
    {
        return ProseFormat.AccessibilityPrefix(accessibility).Trim();
    }
}
