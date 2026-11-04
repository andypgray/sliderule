using SlideRule.Internal;
using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>
///     The projects this run did not survey (not C#, shared, a Web Site, or not built by the default
///     configuration), each with the cure a selection anchored on it reports in place of shape advice.
/// </summary>
/// <remarks>
///     <para>
///         A selection anchored on such a project matches nothing however it is spelled, so advice about its spelling
///         sends the reader after a typo that is not there. The rule still fails: unlike a narrowed run, no other run
///         of the same build surveys the project, so a skip would leave a rule that is never judged.
///     </para>
///     <para>
///         The cures arrive composed, keyed by the project name a selection names, for the reason
///         <see cref="IncompleteModel" /> gives: the wording lives with the stamps every verb writes. Null on a run
///         that surveyed every project it found.
///     </para>
/// </remarks>
internal sealed class UnsurveyedProjects
{
    private readonly IReadOnlyDictionary<string, string> _cureByProject;

    internal UnsurveyedProjects(IReadOnlyDictionary<string, string> cureByProject)
    {
        _cureByProject = Guard.NotNull(cureByProject, nameof(cureByProject));
    }

    /// <summary>
    ///     The cure for the first unsurveyed project <paramref name="selection" /> is anchored on, through a layer's
    ///     definition and each part of a union; null when it is anchored on none.
    /// </summary>
    internal string? CureFor(Selection? selection)
    {
        return selection is null
            ? null
            : AnchorsOf(selection)
                .Select(CureFor)
                .FirstOrDefault(cure => cure is not null);
    }

    /// <summary>The cure for the first unsurveyed project a project selection names; null when it names none.</summary>
    internal string? CureFor(ProjectSelection? selection)
    {
        // Only a Named list names a project exactly; a glob that matches nothing is a spelling question still.
        return selection?.Adjectives
            .OfType<ProjectNamedAdjective>()
            .SelectMany(named => named.Names)
            .Select(CureFor)
            .FirstOrDefault(cure => cure is not null);
    }

    private string? CureFor(string projectName)
    {
        return _cureByProject.TryGetValue(projectName, out string? cure) ? cure : null;
    }

    private static IEnumerable<string> AnchorsOf(Selection selection)
    {
        return selection switch
        {
            UnionSelection union => union.Parts.SelectMany(AnchorsOf),
            _ => selection.Noun switch
            {
                ProjectNoun project => [project.Name],
                LayerNoun { Definition: { } definition } => AnchorsOf(definition),
                _ => []
            }
        };
    }
}
