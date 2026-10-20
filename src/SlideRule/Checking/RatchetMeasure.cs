namespace SlideRule.Checking;

/// <summary>
///     What the ratchet found in a rule's captured baseline section beyond the violations it grandfathered.
/// </summary>
/// <remarks>
///     <see langword="default" /> is the zero triple, which an unratcheted rule reports. All three counts are
///     required: a trailing default would let a single argument mean whichever of them the caller had in mind.
/// </remarks>
internal readonly struct RatchetMeasure(int stale, int shrunk, int uncounted)
{
    /// <summary>Captured entries no current violation matched — fixed debt awaiting acceptance.</summary>
    public int Stale { get; } = stale;

    /// <summary>Matched edge entries whose observed site count is below the recorded one.</summary>
    public int Shrunk { get; } = shrunk;

    /// <summary>
    ///     Matched entries recording no site count, grandfathered at pair or project grain: an edge entry with
    ///     none, or a project-wide entry standing in for the project's packages or frameworks.
    /// </summary>
    public int Uncounted { get; } = uncounted;
}
