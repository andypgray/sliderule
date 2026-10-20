using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     A <em>family</em> — <c>arch.Each(dispatch, tracking, invoicing)</c> or
///     <c>arch.Each(arch.Projects.Matching("Nop.Plugin.*"))</c> — a noun that carries a partition of the
///     types it names into <em>cells</em>, copy by copy where several projects declare one type
///     (GRAMMAR §5.1). Reference fragment: "each of the Dispatch, Tracking and Invoicing layers", "each of
///     the projects matching <c>`Nop.Plugin.*`</c>". A bare family speaks in the collective voice
///     (GRAMMAR §6).
/// </summary>
/// <remarks>
///     The cells are read only in the places GRAMMAR §5.1 lists; to every other verb, walk and renderer a
///     family is the union of its cells. A one-cell layer family renders exactly as the cell would, in both
///     positions, while the family verbs and the per-cell self still apply to it (the §5.1 union identity); the
///     project form's cell count is a codebase fact rather than a spec one, so its sentence always says
///     "each of".
/// </remarks>
internal sealed class EachNoun : SelectionNoun
{
    internal EachNoun(IReadOnlyList<Layer> layers)
    {
        Layers = layers;
    }

    internal EachNoun(ProjectSelection projects)
    {
        Projects = projects;
    }

    /// <summary>The declared layer cells, or null for the project form.</summary>
    internal IReadOnlyList<Layer>? Layers { get; }

    /// <summary>The project selection whose projects are the cells, or null for the layer form.</summary>
    internal ProjectSelection? Projects { get; }

    /// <summary>The singular noun one cell is — "layer" or "project" — the word the verb phrases carry.</summary>
    internal string CellWord => Layers is null ? "project" : "layer";

    internal override string Locative => " in " + ReferenceFragment;

    internal override string ReferenceFragment => Layers is { } cells ? LayerFragment(cells) : ProjectFragment();

    private static string LayerFragment(IReadOnlyList<Layer> cells)
    {
        if (cells.Count == 1) return cells[0].Noun.ReferenceFragment;

        List<string> names = cells.Select(cell => cell.Name).ToList();
        return $"each of the {ProseFormat.JoinReferencesAnd(names)} layers";
    }

    // The project selection's own phrase, which already reads as a plural noun ("projects matching `X`",
    // "projects `A` or `B`"), so the family prefixes the quantifier and nothing else.
    private string ProjectFragment()
    {
        return "each of the " + Projects!.ReferenceProse();
    }
}
