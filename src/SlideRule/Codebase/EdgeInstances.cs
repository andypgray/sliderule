namespace SlideRule.Codebase;

/// <summary>One instance of a model edge, as one project's compilation made it (GRAMMAR §4.1).</summary>
internal readonly struct EdgeInstance
{
    internal EdgeInstance(string sourceProject, string targetProject, IReadOnlyList<SourceLocation> sites)
    {
        SourceProject = sourceProject;
        TargetProject = targetProject;
        Sites = sites;
    }

    /// <summary>The declarer of the edge's source that compiled this instance.</summary>
    internal string SourceProject { get; }

    /// <summary>The project whose copy of the edge's target this instance reached.</summary>
    internal string TargetProject { get; }

    /// <summary>The sites this instance made the edge at — the lines a rule that owns it may cite.</summary>
    /// <remarks>
    ///     <para>
    ///         A subsequence of the corresponding edge-level list, BY REFERENCE: every element here is an
    ///         element of that list, in that list's order, and where this instance made every site it IS
    ///         that list (the same object). "Corresponding" is per list: an element of an instance from
    ///         <c>Evidence</c> belongs to the edge's <c>Sites</c>, one from a catch edge's
    ///         <c>UnfilteredEvidence</c> to its <c>UnfilteredSites</c>.
    ///     </para>
    ///     <para>
    ///         That is what makes a violation citable: a rule anchored on one declarer prints these lines
    ///         alone, so two copies of one name that drifted apart never cite each other's. The list is
    ///         empty for an instance that made none of a narrowing's sites, such as a copy whose every
    ///         <c>catch</c> of the type carries a filter.
    ///     </para>
    /// </remarks>
    internal IReadOnlyList<SourceLocation> Sites { get; }

    /// <summary>
    ///     Whether this instance stayed inside one project — the source compiled its own copy of the
    ///     target, so no project file declares a dependency for it.
    /// </summary>
    internal bool IsIntraProject => string.Equals(SourceProject, TargetProject, StringComparison.Ordinal);
}

/// <summary>
///     The instances one model edge stands for (GRAMMAR §4.1): the ones the merge recorded where either end
///     is a name several projects declare, and otherwise the edge itself, made at its source's project and
///     reaching its target's.
/// </summary>
/// <remarks>
///     The checker walks the same record directly to decide which instances a rule owns, and never through
///     here: it asks per candidate edge per rule, and <see cref="Of" /> builds a one-element list for every
///     edge with no record. The checker and the survey must keep reading that one record, or a verdict and the
///     survey that led an author to it can disagree about which project made a reference.
/// </remarks>
internal static class EdgeInstances
{
    internal static IReadOnlyList<EdgeInstance> Of(ReferenceEdge edge)
    {
        return edge.Evidence.Instances ?? [new EdgeInstance(edge.Source.ProjectName, edge.Target.ProjectName, edge.Sites)];
    }
}
