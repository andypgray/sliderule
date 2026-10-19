using SlideRule.Internal;

namespace SlideRule.Rendering;

/// <summary>
///     The project filter a diagram is drawn through: an allow-list and a deny-list of project-name
///     globs. An empty <see cref="Only" /> means every project, and <see cref="Exclude" /> always wins. A
///     pattern is matched against the project (assembly) name as one whole token, case-sensitively, with
///     <c>*</c> standing for any run of characters, dots included, so <c>MyApp.*</c> reaches
///     <c>MyApp.Web.Api</c>. Filtering is how a large solution keeps a drawing legible, and how a
///     committed diagram stays put while sample or fixture projects come and go.
/// </summary>
internal sealed class DiagramScope
{
    /// <summary>
    ///     Creates a filter from an allow-list and a deny-list of project-name globs. Neither list may be
    ///     null; pass two empty ones, or use <see cref="Everything" />, to filter nothing.
    /// </summary>
    /// <param name="only">The allow-list globs; empty means every project is in scope.</param>
    /// <param name="exclude">
    ///     The deny-list globs; a match drops the project whatever <paramref name="only" /> says.
    /// </param>
    public DiagramScope(IReadOnlyList<string> only, IReadOnlyList<string> exclude)
    {
        Only = Guard.NotNull(only, nameof(only));
        Exclude = Guard.NotNull(exclude, nameof(exclude));
    }

    /// <summary>
    ///     Gets the filter that keeps every project.
    /// </summary>
    public static DiagramScope Everything { get; } = new([], []);

    /// <summary>
    ///     Gets the allow-list globs; empty means every project is in scope.
    /// </summary>
    private IReadOnlyList<string> Only { get; }

    /// <summary>
    ///     Gets the deny-list globs; a match here drops the project whatever <see cref="Only" /> says.
    /// </summary>
    private IReadOnlyList<string> Exclude { get; }

    /// <summary>
    ///     Reports whether <paramref name="projectName" /> survives both lists.
    /// </summary>
    /// <param name="projectName">The project (assembly) name to test.</param>
    /// <returns>
    ///     <see langword="true" /> when no <see cref="Exclude" /> pattern matches it and either
    ///     <see cref="Only" /> is empty or one of its patterns does.
    /// </returns>
    public bool Includes(string projectName)
    {
        Guard.NotNull(projectName, nameof(projectName));

        if (Wildcard.MatchesAny(Exclude, projectName)) return false;

        return Only.Count == 0 || Wildcard.MatchesAny(Only, projectName);
    }
}
