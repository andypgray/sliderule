using SlideRule.Hosting;

namespace SlideRule.Rendering;

/// <summary>
///     Where a scope's context card goes: the scope's ID, the rule the card is rendered from, and the card's
///     destination.
/// </summary>
internal sealed class ScopePlacement
{
    internal ScopePlacement(string scopeId, ArchRule rule, CardDestination destination)
    {
        ScopeId = scopeId;
        Rule = rule;
        Destination = destination;
    }

    /// <summary>
    ///     Gets the scope's ID as the spec declares it, <c>legacy/billing</c> say.
    /// </summary>
    public string ScopeId { get; }

    /// <summary>
    ///     Gets the rule the card is rendered from: a quarantine's containment rule, or a caution's tripwire, which is
    ///     the only rule a caution has.
    /// </summary>
    public ArchRule Rule { get; }

    /// <summary>
    ///     Gets the directory whose <c>AGENTS.md</c> receives the scope card, or the reason no directory does.
    /// </summary>
    public CardDestination Destination { get; }
}
