using SlideRule.Model;

namespace SlideRule.Prose;

/// <summary>
///     Low-level prose formatting primitives for the law sentence (GRAMMAR §6), held in one place so
///     that every multi-operand list — type, attribute and member alike — backticks, joins and
///     qualifies colliding simple names identically.
/// </summary>
/// <remarks>
///     The anchor helpers read an anchor's simple display or its path, never a <see cref="Type" />, so a
///     string anchor renders and widens exactly as its <c>typeof</c> twin does.
/// </remarks>
internal static class ProseFormat
{
    internal static string Backtick(string value)
    {
        return "`" + value + "`";
    }

    internal static string Capitalize(string phrase)
    {
        if (string.IsNullOrEmpty(phrase)) return phrase;

        return char.ToUpperInvariant(phrase[0]) + phrase.Substring(1);
    }

    /// <summary>
    ///     The premodifier an <see cref="Accessibility" /> puts on the subject head (GRAMMAR §5.2):
    ///     "public types", "private types". The trailing space is part of the fragment, as it is for
    ///     every head prefix.
    /// </summary>
    internal static string AccessibilityPrefix(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public ",
            Accessibility.Internal => "internal ",
            Accessibility.Protected => "protected ",
            Accessibility.ProtectedInternal => "protected internal ",
            Accessibility.PrivateProtected => "private protected ",
            Accessibility.Private => "private ",
            _ => ""
        };
    }

    /// <summary>The plural noun a <see cref="TypeKind" /> substitutes for the subject head (GRAMMAR §5.2).</summary>
    internal static string KindPlural(TypeKind kind)
    {
        return kind switch
        {
            TypeKind.Class => "classes",
            TypeKind.Interface => "interfaces",
            TypeKind.Struct => "structs",
            TypeKind.Enum => "enums",
            TypeKind.Delegate => "delegates",
            _ => "types"
        };
    }

    /// <summary>
    ///     The registration noun's reference fragment (GRAMMAR §5.1): the lifetime-prefixed head, or the
    ///     bare "registered types" for any lifetime (<c>null</c>).
    /// </summary>
    /// <remarks>
    ///     An undefined lifetime (refused at spec build, GRAMMAR §8 item 19) never reaches a render, so
    ///     it falls back to the bare fragment.
    /// </remarks>
    internal static string RegisteredFragment(Lifetime? lifetime)
    {
        return lifetime switch
        {
            Lifetime.Singleton => "singleton-registered types",
            Lifetime.Scoped => "scoped-registered types",
            Lifetime.Transient => "transient-registered types",
            _ => "registered types"
        };
    }

    /// <summary>The plural noun a member <see cref="MemberKindFilter" /> projection heads with (GRAMMAR §5.7).</summary>
    internal static string MemberKindPlural(MemberKindFilter kind)
    {
        return kind switch
        {
            MemberKindFilter.Method => "methods",
            MemberKindFilter.Property => "properties",
            MemberKindFilter.Field => "fields",
            MemberKindFilter.Event => "events",
            _ => "members"
        };
    }

    /// <summary>
    ///     The backticked, bracketed attribute form with a trailing <c>Attribute</c> stripped:
    ///     <c>ApiControllerAttribute</c> → <c>`[ApiController]`</c> (GRAMMAR §5.2).
    /// </summary>
    internal static string BacktickedAttributeName(this TypeAnchor anchor)
    {
        string bracketed = BracketAttribute(anchor.SimpleDisplay);
        return Backtick(bracketed);
    }

    /// <summary>
    ///     Takes an already-resolved name so a colliding attribute list can bracket its widened form:
    ///     <c>Billing.AuditAttribute</c> → <c>[Billing.Audit]</c>.
    /// </summary>
    private static string BracketAttribute(string name)
    {
        const string suffix = "Attribute";
        if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - suffix.Length);

        return "[" + name + "]";
    }

    internal static string BacktickedName(this TypeAnchor anchor)
    {
        return Backtick(anchor.SimpleDisplay);
    }

    /// <summary>
    ///     Joins the anchors' backticked names as an or-list, <c>`A` or `B`</c> (GRAMMAR §5.3, §6), widened
    ///     where they collide (<see cref="ResolvePathDisplays" />).
    /// </summary>
    internal static string AnchorList(IReadOnlyList<TypeAnchor> anchors)
    {
        List<IReadOnlyList<string>> paths = anchors.Select(anchor => anchor.PathSegments).ToList();
        IReadOnlyList<string> displays = ResolvePathDisplays(paths);
        return BacktickedList(displays);
    }

    /// <summary>
    ///     Joins the anchors' attribute names, bracketed and backticked, as an or-list (GRAMMAR §5.3, §6);
    ///     colliding names widen inside the brackets: <c>`[Billing.Audit]` or `[Sales.Audit]`</c>.
    /// </summary>
    internal static string AttributeList(IReadOnlyList<TypeAnchor> anchors)
    {
        // Collision keys on the anchor's simple name; a Foo/FooAttribute pair that shares a bracket
        // display (distinct simple names) is not widened — an accepted v1 corner.
        List<IReadOnlyList<string>> paths = anchors.Select(anchor => anchor.PathSegments).ToList();
        IReadOnlyList<string> displays = ResolvePathDisplays(paths);
        return BacktickedList(displays.Select(BracketAttribute).ToList());
    }

    /// <summary>The one spelling of "these names, as code": each value backticked, joined as an or-list.</summary>
    internal static string BacktickedList(IReadOnlyList<string> values)
    {
        return JoinReferences(values.Select(Backtick).ToList());
    }

    /// <summary>The one spelling of "these names, quoted": each value single-quoted, joined as an and-list.</summary>
    internal static string JoinQuotedAnd(IReadOnlyList<string> values)
    {
        List<string> quoted = values.Select(Quote).ToList();
        return JoinReferencesAnd(quoted);
    }

    internal static string JoinQuoted(IReadOnlyList<string> values)
    {
        List<string> quoted = values.Select(Quote).ToList();
        return JoinReferences(quoted);
    }

    private static string Quote(string value)
    {
        return "'" + value + "'";
    }

    /// <summary>
    ///     Joins already-rendered reference fragments with no Oxford comma (GRAMMAR §6):
    ///     2 → <c>`A` or `B`</c>; 3+ → <c>`A`, `B` or `C`</c>.
    /// </summary>
    internal static string JoinReferences(IReadOnlyList<string> references)
    {
        return JoinReferences(references, closeBeforeOr: false);
    }

    /// <summary>
    ///     The same join with the final joiner closing an <c>Except</c> parenthetical the penultimate item
    ///     left open: <c>`A`, `B`, or `C`</c> (GRAMMAR §6).
    /// </summary>
    internal static string JoinReferences(IReadOnlyList<string> references, bool closeBeforeOr)
    {
        return Join(references, closeBeforeOr ? ", or " : " or ");
    }

    /// <summary>
    ///     The and-joined twin of <see cref="JoinReferences(IReadOnlyList{string})" />, with no Oxford comma
    ///     either (GRAMMAR §6): <c>A, B and C</c>. A family's cell list reads with it, where an or-list would say
    ///     the rule ranges over one of them.
    /// </summary>
    internal static string JoinReferencesAnd(IReadOnlyList<string> references)
    {
        return Join(references, " and ");
    }

    /// <summary>
    ///     The inventory join, for a list that is a set of things rather than a sentence naming an
    ///     alternative: <c>A, B, C</c>. A quarantine's sanctioned surface reads this way wherever it prints,
    ///     over the fragments <see cref="SentenceRenderer.ReferenceFragments" /> renders.
    /// </summary>
    internal static string JoinInventory(IReadOnlyList<string> values)
    {
        return string.Join(", ", values);
    }

    private static string Join(IReadOnlyList<string> references, string joiner)
    {
        switch (references.Count)
        {
            case 0:
                return string.Empty;
            case 1:
                return references[0];
            default:
                string head = string.Join(", ", references.Take(references.Count - 1));
                return head + joiner + references[references.Count - 1];
        }
    }

    /// <summary>
    ///     The collision primitive itself (GRAMMAR §6), over bare dot-separated paths: each path renders
    ///     as its last segment, and a set of paths sharing that last segment widens outward together —
    ///     by the minimal number of trailing segments that tells them apart — until distinct.
    /// </summary>
    /// <returns>Displays positionally aligned with <paramref name="paths" />.</returns>
    internal static IReadOnlyList<string> ResolvePathDisplays(IReadOnlyList<IReadOnlyList<string>> paths)
    {
        var displays = new string[paths.Count];
        foreach (IGrouping<string, int>? group in Enumerable.Range(0, paths.Count).GroupBy(index => Leaf(paths[index])))
        {
            List<int> indices = group.ToList();

            // Dedupe by full path before judging the group: the SAME operand written twice is one
            // member, not a collision — and widening could never separate it, so a naive count would
            // qualify every repeated operand to its full path.
            int memberCount = indices.Select(index => Qualify(paths[index], paths[index].Count)).Distinct().Count();
            if (memberCount == 1)
            {
                foreach (int index in indices) displays[index] = Leaf(paths[index]);

                continue;
            }

            int maxDepth = indices.Max(index => paths[index].Count);
            int chosen = maxDepth;
            for (var count = 1; count <= maxDepth; count++)
                if (indices.Select(index => Qualify(paths[index], count)).Distinct().Count() == memberCount)
                {
                    chosen = count;
                    break;
                }

            foreach (int index in indices) displays[index] = Qualify(paths[index], chosen);
        }

        return displays;
    }

    private static string Leaf(IReadOnlyList<string> segments)
    {
        return segments[segments.Count - 1];
    }

    private static string Qualify(IReadOnlyList<string> segments, int segmentCount)
    {
        int take = Math.Min(Math.Max(segmentCount, 1), segments.Count);
        return string.Join(".", segments.Skip(segments.Count - take));
    }
}
