using SlideRule.Internal;

namespace SlideRule.Rendering;

/// <summary>
///     The project filter a diagram is drawn through: an allow-list and a deny-list of project-name
///     globs. An empty <see cref="Only" /> means every project, and <see cref="Exclude" /> always wins. A
///     pattern is matched against the project (assembly) name as one whole token, case-sensitively, with
///     <c>*</c> standing for any run of characters, dots included, so <c>MyApp.*</c> reaches
///     <c>MyApp.Web.Api</c>.
/// </summary>
internal sealed class DiagramScope
{
    public DiagramScope(IReadOnlyList<string> only, IReadOnlyList<string> exclude)
    {
        Only = Guard.NotNull(only, nameof(only));
        Exclude = Guard.NotNull(exclude, nameof(exclude));
    }

    public static DiagramScope Everything { get; } = new([], []);

    private IReadOnlyList<string> Only { get; }

    private IReadOnlyList<string> Exclude { get; }

    public bool Includes(string projectName)
    {
        Guard.NotNull(projectName, nameof(projectName));

        if (Wildcard.MatchesAny(Exclude, projectName)) return false;

        return Only.Count == 0 || Wildcard.MatchesAny(Only, projectName);
    }
}
