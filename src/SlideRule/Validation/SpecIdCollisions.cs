using Code = SlideRule.Validation.SpecValidationErrorCode;

namespace SlideRule.Validation;

/// <summary>
///     What a spec's declared rule and scope IDs decide between them (GRAMMAR §8 items 1 and 7): a rule
///     ID reaching into a scope's reserved namespace, and a duplicate in the post-desugar ID set.
/// </summary>
/// <remarks>
///     Verdicts come back in the order the catalog reports them: every extends-scope verdict, in rules ×
///     scopes order, then every duplicate in source order. A malformed ID takes part like any other,
///     because a spec that spells an ID wrongly twice has still written it twice.
/// </remarks>
internal static class SpecIdCollisions
{
    /// <summary>
    ///     The collisions a set of declarations carries, or an empty list when the IDs are all distinct.
    /// </summary>
    /// <param name="declarations">Every rule and scope ID a spec declared, in authoring order.</param>
    internal static IReadOnlyList<CollisionVerdict> Decide(IReadOnlyList<IdDeclaration> declarations)
    {
        List<IdDeclaration> rules = declarations.Where(declaration => declaration.Role == IdRole.Rule).ToList();
        List<IdDeclaration> scopes = declarations.Where(declaration => declaration.Role == IdRole.Scope).ToList();
        var verdicts = new List<CollisionVerdict>();

        // The full cross product: a rule ID may equal a scope ID or fall beneath its reserved prefix, and
        // either way the generated children have nowhere to live.
        var extendsScope = new HashSet<string>(StringComparer.Ordinal);
        foreach (IdDeclaration rule in rules)
        foreach (IdDeclaration scope in scopes)
            if (RuleIdSyntax.ExtendsScope(rule.Id, scope.Id))
            {
                verdicts.Add(new CollisionVerdict(Code.IdExtendsScope, rule.Id,
                    $"Rule ID '{rule.Id}' extends scope '{scope.Id}'; the '{scope.Id}/…' namespace is reserved for its generated children.",
                    [rule.Tag, scope.Tag]));
                extendsScope.Add(rule.Id);
            }

        // Duplicate detection over the post-desugar ID set. IDs already flagged as extends-scope are
        // excluded so they are reported once, by the more specific code.
        var postDesugar = new List<(string Id, int Tag)>();
        foreach (IdDeclaration rule in rules)
            if (!extendsScope.Contains(rule.Id))
                postDesugar.Add((rule.Id, rule.Tag));

        // Both children are reserved for every scope, whatever its posture — a caution mints only the
        // tripwire, but reserving the containment ID too is what lets a caution be promoted to a quarantine
        // later without the ID it grows into having been taken meanwhile. So two scopes declared with one
        // ID collide twice, on two IDs that appear nowhere in the source.
        foreach (IdDeclaration scope in scopes)
        {
            postDesugar.Add((scope.Id + "/containment", scope.Tag));
            postDesugar.Add((scope.Id + "/tripwire", scope.Tag));
        }

        foreach (IGrouping<string, (string Id, int Tag)> group in postDesugar.GroupBy(entry => entry.Id, StringComparer.Ordinal))
        {
            List<int> tags = group.Select(entry => entry.Tag).ToList();
            if (tags.Count > 1)
                verdicts.Add(new CollisionVerdict(Code.DuplicateId, group.Key, $"Duplicate rule ID '{group.Key}'.", tags));
        }

        return verdicts;
    }
}
