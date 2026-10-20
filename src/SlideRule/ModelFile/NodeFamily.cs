namespace SlideRule.ModelFile;

/// <summary>
///     Which stratum a discriminated node belongs to. Discriminators are only unique within a family, so
///     a reader dispatches on the pair.
/// </summary>
internal enum NodeFamily
{
    /// <summary>A <c>SelectionNoun</c>.</summary>
    Noun,

    /// <summary>A <c>SelectionAdjective</c>.</summary>
    Adjective,

    MemberAdjective,

    ProjectAdjective,

    /// <summary>A <c>Constraint</c>, of any of the three strata.</summary>
    Constraint
}
