using SlideRule.Hosting;

namespace SlideRule.Rendering;

/// <summary>Where a scope's context card goes, and the rule the card is rendered from.</summary>
internal sealed class ScopePlacement
{
    internal ScopePlacement(ArchRule rule, CardDestination destination)
    {
        Rule = rule;
        Destination = destination;
    }

    public ArchRule Rule { get; }

    public CardDestination Destination { get; }
}
