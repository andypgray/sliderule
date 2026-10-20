namespace SlideRule.Model;

/// <summary>
///     A refinement on a <see cref="ProjectSelection" />, the project twin of <see cref="SelectionAdjective" />
///     (GRAMMAR §4.10), taking every <see cref="AdjectivePlacement" />.
/// </summary>
internal abstract class ProjectAdjective
{
    internal abstract AdjectivePlacement Placement { get; }

    /// <summary>
    ///     As <see cref="SelectionAdjective.Fragment" />, except that a <see cref="AdjectivePlacement.Head" />
    ///     fragment is the whole phrase that replaces the head.
    /// </summary>
    internal abstract string Fragment { get; }

    /// <summary>The project twin of <see cref="SelectionAdjective.OpensParenthetical" />.</summary>
    internal virtual bool OpensParenthetical => false;
}
