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
///         each selection's own shape advice. It is the fact about the model rather than about the exit code,
///         so it is present whether or not the operator opted into checking the partial model: the hint is
///         true either way, and the surface that opted in is the one surface where the verdict still prints.
///     </para>
///     <para>
///         The sentence arrives composed rather than assembled here, for
///         <see cref="NarrowedUniverse" />'s two reasons: the load-failure lexicon lives with the stamps
///         every verb writes, so a wording minted in Core would drift from them silently; and Core cannot
///         relativize or name a project path anyway (no <c>Path.GetRelativePath</c> on netstandard2.0). The
///         failed and unrestored projects are therefore the composer's inputs rather than this value's
///         contents: what a checked rule needs is the sentence.
///     </para>
/// </remarks>
internal sealed class IncompleteModel
{
    /// <summary>Builds the fact from the one-line cure a rule whose selection matched nothing reports.</summary>
    /// <param name="emptySelectionHint">
    ///     The cure an empty subject or an inert target carries in place of its shape advice.
    /// </param>
    internal IncompleteModel(string emptySelectionHint)
    {
        EmptySelectionHint = Guard.NotNull(emptySelectionHint, nameof(emptySelectionHint));
    }

    /// <summary>
    ///     The cure a selection that matched nothing reports while the model is partial, in place of the
    ///     shape advice rather than beside it (<see cref="AuthoringHints" /> says why). It names the failed
    ///     projects by count and the repair, never the paths — those ride the stamp above the report, one
    ///     place to read them rather than one copy per rule, which is
    ///     <see cref="NarrowedUniverse.RuleSkipReason" />'s reasoning for the same shape.
    /// </summary>
    internal string EmptySelectionHint { get; }
}
