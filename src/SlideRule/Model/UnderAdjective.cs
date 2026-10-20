using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Under("src/Widgets")</c> → " under `src/Widgets`", or " under `A` or `B`" for several (GRAMMAR §5.2): the
///     types any one of whose declaring files sits under one of the path globs, read relative to the solution
///     directory.
/// </summary>
internal sealed class UnderAdjective(IReadOnlyList<string> globs) : SelectionAdjective
{
    /// <summary>The path globs, in authoring order.</summary>
    internal IReadOnlyList<string> Globs { get; } = globs;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => " under " + ProseFormat.BacktickedList(Globs);
}
