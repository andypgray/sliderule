namespace SlideRule.Model;

/// <summary>
///     The node classes that hold a delegate: the <c>Where</c> adjectives and the <c>Must</c> verbs. Each
///     stores its predicate unevaluated until the check runs, and renders its required description verbatim in
///     place of the lambda.
/// </summary>
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

    internal static IReadOnlyCollection<Type> Types => Set;

    internal static bool Contains(Type nodeType)
    {
        return Set.Contains(nodeType);
    }
}
