
namespace SlideRule.Model;

/// <summary>
///     A refinement on a <see cref="MemberSelection" /> — a closed hierarchy so each member adjective
///     owns its own prose fragment (GRAMMAR §2 admission rule, §4.6). Reuses
///     <see cref="AdjectivePlacement" /> minus <see cref="AdjectivePlacement.Head" />: the projection
///     fixes the head, so no member adjective substitutes it (§6).
/// </summary>
internal abstract class MemberAdjective
{
    /// <summary>Where this adjective's fragment lands during member-subject assembly (GRAMMAR §6).</summary>
    internal abstract AdjectivePlacement Placement { get; }

    /// <summary>The rendered fragment, including its leading separator (e.g. <c> returning `Task`</c>).</summary>
    internal abstract string Fragment { get; }

    /// <summary>
    ///     Whether the fragment opens an <c>Except</c> parenthetical it leaves for the composer to close
    ///     (GRAMMAR §6) — the member twin of <see cref="SelectionAdjective.OpensParenthetical" />, true for
    ///     <see cref="MemberExceptAdjective" /> alone.
    /// </summary>
    internal virtual bool OpensParenthetical => false;
}
