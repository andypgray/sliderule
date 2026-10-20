using SlideRule.Hosting;

namespace SlideRule.Rendering;

/// <summary>Where a scope's context card goes, and the rule the card is rendered from.</summary>
internal sealed class ScopePlacement
{
    internal ScopePlacement(string scopeId, ArchRule rule, CardDestination destination)
    {
        ScopeId = scopeId;
        Rule = rule;
        Destination = destination;
    }

    public string ScopeId { get; }

    public ArchRule Rule { get; }

    public CardDestination Destination { get; }
}
