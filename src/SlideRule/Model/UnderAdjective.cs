using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Under("src/Widgets")</c> → " under `src/Widgets`", or " under `A` or `B`" for several (GRAMMAR §5.2): the
///     types any one of whose declaring files sits under one of the path globs, read relative to the solution
///     directory.
/// </summary>
/// <remarks>
///     An inline reduced relative clause, like <see cref="NamedAdjective" />: where a type is declared is a fact
///     about the type, read after the head and its locative ("types in `MyApp.*` under `src`"), and the backticked
///     globs are what the checker matches, case aside.
/// </remarks>
internal sealed class UnderAdjective(IReadOnlyList<string> globs) : SelectionAdjective
{
    /// <summary>The path globs, in authoring order.</summary>
    internal IReadOnlyList<string> Globs { get; } = globs;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => " under " + ProseFormat.BacktickedList(Globs);
}
