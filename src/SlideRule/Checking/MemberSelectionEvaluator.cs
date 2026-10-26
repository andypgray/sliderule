using SlideRule.Codebase;
using SlideRule.Fluent;
using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>
///     Resolves a <see cref="MemberSelection" /> to the ordered list of <see cref="MemberNode" />s it
///     names (GRAMMAR §4.6): the declared members of its underlying type selection, narrowed by the
///     projection's kind filter and then by each member adjective in authoring order.
/// </summary>
internal static class MemberSelectionEvaluator
{
    /// <summary>
    ///     The declared members the selection ranges over, deterministically ordered, taken from a
    ///     source-type set the caller has already evaluated in subject position (so a union member subject
    ///     is not evaluated twice). An <c>Except</c> payload's source is resolved through
    ///     <paramref name="selections" />, since nothing has evaluated it yet. Throws
    ///     <see cref="RuleEvaluationException" /> when a <c>Returning</c> anchor, the selection's own or a
    ///     payload's, is a closed generic construction (the check-time backstop for GRAMMAR §8 item 14).
    /// </summary>
    internal static IReadOnlyList<MemberNode> Resolve(
        MemberSelection selection, HashSet<TypeNode> sourceTypes, SelectionEvaluator selections)
    {
        return Narrowed(selection, sourceTypes, selections)
            .OrderBy(member => member.DeclaringType.FullName, StringComparer.Ordinal)
            .ThenBy(member => member.SymbolId, StringComparer.Ordinal)
            .ToList();
    }

    // The members a selection keeps, unordered. Every adjective that can throw does so while the chain is built,
    // before anything is enumerated, so a caller that never enumerates still meets the fault.
    private static IEnumerable<MemberNode> Narrowed(
        MemberSelection selection, IEnumerable<TypeNode> sourceTypes, SelectionEvaluator selections)
    {
        IEnumerable<MemberNode> members = sourceTypes.SelectMany(type => type.Members).Where(KindFilter(selection.Kind));
        foreach (MemberAdjective adjective in selection.Adjectives) members = ApplyAdjective(members, adjective, selections);

        return members;
    }

    // What an Except removes (GRAMMAR §5.7): every member any payload names. Each payload's source resolves
    // where an exclusion stands, in target position, as a type-side Except payload does (GRAMMAR §4.1). Built
    // eagerly, so a payload whose adjective cannot be evaluated throws during resolution exactly as the
    // subject's own would. Subtraction is by node: a member is one MemberNode wherever a selection reaches it.
    private static HashSet<MemberNode> Excluded(IReadOnlyList<MemberSelection> payloads, SelectionEvaluator selections)
    {
        var excluded = new HashSet<MemberNode>();
        foreach (MemberSelection payload in payloads)
        {
            HashSet<TypeNode> sources = selections.Evaluate(payload.Source, SelectionPosition.Target);
            excluded.UnionWith(Narrowed(payload, sources, selections));
        }

        return excluded;
    }

    private static Func<MemberNode, bool> KindFilter(MemberKindFilter kind)
    {
        return kind switch
        {
            MemberKindFilter.Method => member => member.Kind == MemberKind.Method,
            MemberKindFilter.Property => member => member.Kind == MemberKind.Property,
            MemberKindFilter.Field => member => member.Kind == MemberKind.Field,
            MemberKindFilter.Event => member => member.Kind == MemberKind.Event,
            _ => _ => true // MemberKindFilter.Any — the unrestricted .Members projection
        };
    }

    private static IEnumerable<MemberNode> ApplyAdjective(
        IEnumerable<MemberNode> current, MemberAdjective adjective, SelectionEvaluator selections)
    {
        switch (adjective)
        {
            case MemberWithSuffixAdjective suffix:
                return current.Where(member => member.Name.EndsWith(suffix.Suffix, StringComparison.Ordinal));
            case MemberWithPrefixAdjective prefix:
                return current.Where(member => member.Name.StartsWith(prefix.Prefix, StringComparison.Ordinal));
            case MemberWithNameMatchingAdjective matching:
                var pattern = new TypeNamePattern(matching.Glob);
                return current.Where(member => pattern.Matches(member.Name));
            case MemberNamedAdjective named:
                var names = new HashSet<string>(named.Names, StringComparer.Ordinal);
                return current.Where(member => names.Contains(member.Name));
            case ReturningAdjective returning:
                // Anchor keys resolve eagerly (before the lazy Where), so a closed-generic typeof anchor throws
                // the backstop here — during resolution — exactly like a closed-generic type noun (§4.1).
                HashSet<string> anchors = ReturningAnchors(returning.Anchors);
                return current.Where(member => member.ReturnTypeFullName is { } returnType && anchors.Contains(returnType));
            case MemberAttributedWithAdjective attributed:
                // The matcher is built once, eagerly (before the lazy Where), so an unrepresentable typeof
                // anchor throws here — during resolution — exactly like the type-side attribute adjective.
                Func<MemberNode, bool> attributeMatch = MemberAttributeMatcher(attributed.Anchor);
                return current.Where(attributeMatch);
            case MemberThatAreStaticAdjective:
                return current.Where(member => member.IsStatic);
            case MemberAccessibilityAdjective accessibility:
                return current.Where(member => member.Accessibility == accessibility.Accessibility);
            case MemberExceptAdjective except:
                HashSet<MemberNode> excluded = Excluded(except.Payloads, selections);
                return current.Where(member => !excluded.Contains(member));
            case MemberWhereAdjective where:
                return current.Where(member => SelectionEvaluator.InvokePredicate(where.Predicate, member, "Where"));
            default:
                // Fail closed: an unknown member adjective would silently widen the member set. The kind
                // filter's Any arm (KindFilter) is a legitimate total match; a missing adjective arm is a bug —
                // throw (ArchChecker contains it per-rule) rather than pass the un-narrowed set through.
                throw new InvalidOperationException($"Unhandled member adjective '{adjective.GetType().Name}'.");
        }
    }

    /// <summary>
    ///     The declared-attribute matcher for a member (GRAMMAR §4.6, §5.7) — the member twin of
    ///     <see cref="SelectionEvaluator.AttributeMatcher" />, over <see cref="IMemberInfo.Attributes" />
    ///     instead of a type's attribute constructions.
    /// </summary>
    /// <remarks>
    ///     It shares that matcher's arms literally, classifying the anchor through
    ///     <see cref="SelectionEvaluator.AnchorKey" />. Declared attributes only — no inheritance, and no
    ///     accessor or <c>[return:]</c> attributes (they hang off other symbols). The anchor's key is read once,
    ///     eagerly, so an unrepresentable anchor throws before any member is tested.
    /// </remarks>
    internal static Func<IMemberInfo, bool> MemberAttributeMatcher(TypeAnchor anchor)
    {
        (string key, bool onDefinition) = SelectionEvaluator.AnchorKey(anchor);
        Func<IAttributeInfo, string> nameOf = onDefinition ? a => a.DefinitionFullName : a => a.FullName;
        return member =>
        {
            IReadOnlyList<IAttributeInfo> attributes = member.Attributes;
            for (var i = 0; i < attributes.Count; i++)
                if (nameOf(attributes[i]) == key)
                    return true;

            return false;
        };
    }

    // The definition-level FQNs a .Returning anchor set matches against, byte-identical to the extraction's
    // ReturnTypeFullName form (GRAMMAR §4.6): a non-generic anchor is exact, an open-generic anchor matches
    // any construction (its declared-type-parameter definition name), and a string anchor is that same form
    // written out, so it passes through verbatim. DefinitionFullName refuses a closed generic on the typeof
    // arm here — the check-time backstop for the spec-build refusal (GRAMMAR §8 item 14), which is a
    // typeof-arm fact: nothing is inferred from a string's shape, so a constructed spelling is a legal name
    // that matches nothing.
    private static HashSet<string> ReturningAnchors(IReadOnlyList<TypeAnchor> anchors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (TypeAnchor anchor in anchors)
            names.Add(SelectionEvaluator.DefinitionFullName(
                anchor, "member return-type matching is definition-level. Anchor on the open definition instead."));

        return names;
    }
}
