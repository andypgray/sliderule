namespace SlideRule.Codebase;

/// <summary>
///     What one edge fact cites: the sites it occurs at, and the instances that made them, each carrying its
///     share of those sites (GRAMMAR §4.1). <see cref="Instances" /> is <see langword="null" /> for the one
///     instance the edge's endpoints name, which made every site.
/// </summary>
/// <remarks>
///     The two lists are one value because neither means anything without the other: an instance's share is a
///     subsequence of <see cref="Sites" /> BY REFERENCE, and a rule that read one list's instances beside
///     another list's sites would cite lines that no instance it owns ever made.
/// </remarks>
internal readonly struct EdgeEvidence
{
    internal EdgeEvidence(IReadOnlyList<SourceLocation> sites, IReadOnlyList<EdgeInstance>? instances = null)
    {
        Sites = sites;
        Instances = instances;
    }

    /// <summary>
    ///     Each distinct place the fact occurs, ordered by file then line; two occurrences on one line are one
    ///     site.
    /// </summary>
    internal IReadOnlyList<SourceLocation> Sites { get; }

    /// <summary>
    ///     The instances the merge recorded, ordinal by the project that made each and then by the project whose
    ///     copy it reached; set exactly when either end is a name several projects declare.
    /// </summary>
    internal IReadOnlyList<EdgeInstance>? Instances { get; }
}
