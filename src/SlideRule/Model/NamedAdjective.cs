using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.Named(name, …)</c> → " named `Program`", or " named `A` or `B`" for several (GRAMMAR §5.2), matched
///     exact and ordinal on the simple name.
/// </summary>
/// <remarks>
///     The clause states the set the checker matches, every type carrying the name; a bare "`X`" would read as
///     the one type an <c>arch.Type</c> noun names.
/// </remarks>
internal sealed class NamedAdjective(IReadOnlyList<string> names) : SelectionAdjective
{
    /// <summary>The exact type names, in authoring order.</summary>
    internal IReadOnlyList<string> Names { get; } = names;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => " named " + ProseFormat.BacktickedList(Names);
}
