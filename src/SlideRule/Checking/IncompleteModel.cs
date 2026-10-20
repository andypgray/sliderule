using SlideRule.Internal;

namespace SlideRule.Checking;

/// <summary>
///     The fact that this run's model is smaller than the codebase because part of it never loaded — what
///     turns a selection that matched nothing from a spec defect into a selection whose types or edges were
///     never extracted (GRAMMAR §4.1).
/// </summary>
/// <remarks>
///     <para>
///         Null on every run whose projects all loaded and all restored, so a run over a whole model reaches
///         each selection's own shape advice. It is present whether or not the operator opted into checking
///         the partial model, since the hint is true either way.
///     </para>
///     <para>
///         The sentence arrives composed rather than assembled here: the load-failure lexicon lives with the
///         stamps every verb writes, so a wording minted in Core would drift from them silently, and Core
///         cannot relativize or name a project path anyway (no <c>Path.GetRelativePath</c> on netstandard2.0).
///     </para>
/// </remarks>
internal sealed class IncompleteModel
{
    internal IncompleteModel(string emptySelectionHint)
    {
        EmptySelectionHint = Guard.NotNull(emptySelectionHint, nameof(emptySelectionHint));
    }

    /// <summary>
    ///     The cure a selection that matched nothing reports while the model is partial, in place of the
    ///     shape advice rather than beside it (<see cref="AuthoringHints" /> says why). It names the failed
    ///     projects by count and the repair, never the paths, which ride the stamp above the report: one
    ///     place to read them rather than one copy per rule.
    /// </summary>
    internal string EmptySelectionHint { get; }
}
