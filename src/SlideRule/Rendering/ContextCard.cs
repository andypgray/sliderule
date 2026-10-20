namespace SlideRule.Rendering;

/// <summary>
///     One rendered context card and where it goes: a layer card or a scope card, and its destination.
/// </summary>
internal sealed class ContextCard
{
    internal ContextCard(CardDestination destination, string body)
    {
        Destination = destination;
        Body = body;
    }

    public CardDestination Destination { get; }

    public string Body { get; }
}
