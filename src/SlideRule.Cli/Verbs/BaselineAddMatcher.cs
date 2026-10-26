using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Rendering;
using SlideRule.Roslyn;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     Resolves the <c>add</c> names against a rule's current violations, from the same
///     empty-baseline check the whole <c>baseline</c> command runs on.
/// </summary>
/// <remarks>
///     <para>
///         <b>What matches what.</b> Every name is compared ordinally with the spellings of its candidate: the
///         readable full name and the stored symbol ID, such as <c>System.DateTime.Now</c> and
///         <c>P:System.DateTime.Now</c> for a member, whose full name takes no parentheses, and for a member also
///         the display form the no-match listing prints, such as <c>App.Home.Save()</c>. An edge's source
///         and target must match the <em>same</em> violation, and a member full name naming an overloaded
///         method matches every overload, so the ambiguity error lists the distinct IDs to retry with
///         (GRAMMAR §4.5, §4.6).
///     </para>
///     <para>
///         Zero matches and ambiguous matches (two distinct identities) are loud
///         <see cref="UserErrorException" />s listing the candidates — the baseline records observed
///         reality, so the valve only admits what is actually red.
///     </para>
/// </remarks>
internal static class BaselineAddMatcher
{
    public static Violation ResolveEdge(string ruleId, IReadOnlyList<Violation> violations, string source, string target)
    {
        var echo = $"--source '{source}' --target '{target}'";
        List<Violation> candidates = violations
            .Where(v => MatchesEdge(v, source, target))
            .ToList();

        return Resolve(ruleId, violations, candidates, echo);
    }

    public static Violation ResolveSubject(string ruleId, IReadOnlyList<Violation> violations, string subject)
    {
        var echo = $"--subject '{subject}'";
        List<Violation> candidates = violations
            .Where(v => MatchesSubject(v, subject))
            .ToList();

        return Resolve(ruleId, violations, candidates, echo);
    }

    // A project matches only where the violation names no package, framework or property: those are keyed per
    // item, so they take the edge form, and the no-match listing shows each as `Project -> item`.
    private static bool MatchesSubject(Violation violation, string subject)
    {
        return violation.Kind switch
        {
            ViolationKind.Shape => Matches(violation.Subject!, subject),
            ViolationKind.MemberShape => MatchesMemberSubject(violation.SubjectMember!, subject),
            ViolationKind.ProjectShape => violation.ProjectItem is null
                                          && MatchesProjectSubject(violation.SubjectProject!, subject),
            _ => false
        };
    }

    private static bool MatchesMemberSubject(MemberNode member, string subject)
    {
        return NamedBy(subject, $"{member.DeclaringType.FullName}.{member.Name}", member.SymbolId)
               || string.Equals(MemberDisplay.Of(member), subject, StringComparison.Ordinal);
    }

    // A project's bare name or its identity form (GRAMMAR §4.10). The key comes off the node, so this end and the
    // mint that writes the baseline entry cannot spell it apart.
    private static bool MatchesProjectSubject(ProjectNode project, string subject)
    {
        return NamedBy(subject, project.Name, project.SymbolId);
    }

    private static Violation Resolve(string ruleId, IReadOnlyList<Violation> violations, IReadOnlyList<Violation> candidates, string echo)
    {
        List<BaselineEntry> identities = candidates.Select(c => c.BaselineIdentity()!).Distinct().ToList();
        if (identities.Count == 0) throw new UserErrorException(NoMatch(ruleId, violations, echo));
        if (identities.Count > 1)
            throw new UserErrorException(
                $"{echo} matches more than one current violation of '{ruleId}': {RenderSymbols(identities)}. Use the symbol ID form.");

        return candidates[0];
    }

    // Every edge kind but MemberUse matches on both type endpoints, because the second type — constructed
    // (GRAMMAR §4.5), injected (§4.7), caught or thrown (§4.8), exposed (§4.9) — rides the Target slot. For the
    // same reason those kinds need no dedicated FullNameForm arm: the default `Source -> Target` covers them.
    private static bool MatchesEdge(Violation violation, string source, string target)
    {
        return violation.Kind switch
        {
            _ when violation.Kind.IsTypeEdge() => Matches(violation.Source!, source) && Matches(violation.Target!, target),
            ViolationKind.MemberUse => Matches(violation.Source!, source) && MatchesMember(violation.Member!, target),
            ViolationKind.ProjectShape => violation.ProjectItem is { } item
                                          && MatchesProjectSubject(violation.SubjectProject!, source)
                                          && MatchesProjectItem(item, violation.ProjectItemName!, target),
            _ => false
        };
    }

    // A package, framework or property by its bare name or its identity form (package:Serilog, framework:net48,
    // property:Nullable@net8.0).
    private static bool MatchesProjectItem(string item, string itemName, string target)
    {
        return NamedBy(target, itemName, item);
    }

    private static bool MatchesMember(MemberReference member, string target)
    {
        return NamedBy(target, $"{member.ContainingType.FullName}.{member.Name}", member.SymbolId)
               || string.Equals(MemberDisplay.Of(member), target, StringComparison.Ordinal);
    }

    private static string NoMatch(string ruleId, IReadOnlyList<Violation> violations, string echo)
    {
        List<string> current = violations
            .Where(v => v.BaselineIdentity() is not null)
            .Select(FullNameForm)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var head = $"no current violation of '{ruleId}' matches {echo} — the baseline records observed reality";
        if (current.Count == 0) return $"{head}; the rule currently has no violations.";

        string list = string.Join("\n", current.Select(name => $"  {name}"));
        return $"{head}. Current violations:\n{list}";
    }

    private static string RenderSymbols(IEnumerable<BaselineEntry> identities)
    {
        IEnumerable<string> symbols = identities
            .Select(identity => identity.SymbolForm)
            .OrderBy(symbol => symbol, StringComparer.Ordinal);
        return string.Join(", ", symbols);
    }

    // A violation's full-name listing form, shared with BaselineRunner's added-entry echo so the success
    // message and the no-match candidate list render one way for every kind add resolves. Members take
    // the shared display, so a candidate list echoes 'Save()' exactly as 'sliderule check' renders it
    // (GRAMMAR §4.5, §4.6).
    internal static string FullNameForm(Violation violation)
    {
        return violation.Kind switch
        {
            ViolationKind.Shape => violation.Subject!.FullName,
            ViolationKind.MemberShape => MemberDisplay.Of(violation.SubjectMember!),
            ViolationKind.MemberUse => $"{violation.Source!.FullName} -> {MemberDisplay.Of(violation.Member!)}",
            ViolationKind.ProjectShape => violation.ProjectItemName is { } item
                ? $"{violation.SubjectProject!.Name} -> {item}"
                : violation.SubjectProject!.Name,
            _ => $"{violation.Source!.FullName} -> {violation.Target!.FullName}"
        };
    }

    private static bool Matches(TypeNode node, string name)
    {
        return NamedBy(name, node.FullName, node.SymbolId);
    }

    // Whether `given` spells a candidate by its readable name or by its stored symbol ID.
    private static bool NamedBy(string given, string readable, string symbolId)
    {
        return string.Equals(readable, given, StringComparison.Ordinal)
               || string.Equals(symbolId, given, StringComparison.Ordinal);
    }
}
