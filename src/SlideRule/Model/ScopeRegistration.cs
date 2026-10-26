namespace SlideRule.Model;

/// <summary>
///     Mutable backing state for a scope anchor; trailers append to lists, as on <see cref="RuleRegistration" />.
///     A null <see cref="Posture" /> means no posture verb was called (dangling, §8 item 2), and the two verbs
///     set <see cref="Scoped" /> together with it.
/// </summary>
internal sealed class ScopeRegistration(string id) : Registration(id)
{
    internal Selection? Scoped { get; set; }

    /// <summary>The accumulated boundary selections across every <c>BoundaryOnlyVia</c> call.</summary>
    internal List<Selection> Boundary { get; } = [];

    /// <summary>
    ///     How many times <c>BoundaryOnlyVia</c> was called: zero is a hermetic quarantine, a call with no types
    ///     is the empty-boundary error (§8 item 8, reachable through the <c>Type</c> overload alone), and more
    ///     than one is a repeat (§8 item 6).
    /// </summary>
    internal int BoundaryOnlyViaCount { get; set; }

    internal List<string> Dragons { get; } = [];

    internal List<string> DragonsDocs { get; } = [];
}
