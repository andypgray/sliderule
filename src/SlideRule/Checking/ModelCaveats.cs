namespace SlideRule.Checking;

/// <summary>
///     What a run hands the checker about the model it checks, beyond the model itself: the part of it that never
///     loaded, the projects no run surveys, and a universe smaller than the solution. Each is null where the
///     model, the survey or the universe is whole.
/// </summary>
/// <remarks>
///     Every authoring cure reads them all through <see cref="EmptySelectionCure" />, so a cure raised at a new
///     site cannot leave one of them out.
/// </remarks>
internal sealed class ModelCaveats(
    IncompleteModel? incomplete,
    UnsurveyedProjects? unsurveyed,
    NarrowedUniverse? narrowing)
{
    /// <summary>A run over a whole model, a whole survey and the whole solution.</summary>
    internal static ModelCaveats None { get; } = new(null, null, null);

    internal IncompleteModel? Incomplete { get; } = incomplete;

    internal UnsurveyedProjects? Unsurveyed { get; } = unsurveyed;

    internal NarrowedUniverse? Narrowing { get; } = narrowing;

    /// <summary>
    ///     The cure that replaces an empty selection's shape advice, or null where none does: a partial model's
    ///     repair first, then <paramref name="unsurveyedCure" />, then the narrowing's, which only an inert cure
    ///     takes (<paramref name="withNarrowing" />), since a rule whose subject a narrowing emptied is skipped.
    /// </summary>
    internal string? EmptySelectionCure(string? unsurveyedCure, bool withNarrowing)
    {
        return Incomplete?.EmptySelectionHint
               ?? unsurveyedCure
               ?? (withNarrowing ? Narrowing?.InertTargetHint : null);
    }
}
