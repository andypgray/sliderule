namespace SlideRule.Model;

/// <summary>The node classes that hold a delegate: the <c>Where</c> adjectives and the <c>Must</c> verbs.</summary>
internal static class PredicateNodes
{
    private static readonly HashSet<Type> Set =
    [
        typeof(WhereAdjective),
        typeof(MemberWhereAdjective),
        typeof(ProjectWhereAdjective),
        typeof(MustConstraint),
        typeof(MemberMustConstraint),
        typeof(ProjectMustConstraint)
    ];

    /// <summary>Every predicate class, each holding a predicate written in code.</summary>
    internal static IReadOnlyCollection<Type> Types => Set;

    /// <summary>Whether <paramref name="nodeType" /> is one of the predicate classes.</summary>
    internal static bool Contains(Type nodeType)
    {
        return Set.Contains(nodeType);
    }
}
