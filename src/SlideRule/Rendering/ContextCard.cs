namespace SlideRule.Rendering;

/// <summary>
///     One rendered context card and where it goes: a layer card or a scope card, and its destination.
/// </summary>
// The one shape every consumer of scoped context reads, so `render` and the `context` verb's path
// lookup see the same card kinds in the same order, and an unplaceable card is data in both rather
// than a warning in one and a silent drop in the next.
internal sealed class ContextCard
{
    internal ContextCard(CardDestination destination, string body)
    {
        Destination = destination;
        Body = body;
    }

    /// <summary>
    ///     Gets the directory whose <c>AGENTS.md</c> receives <see cref="Body" />, or the reason nothing placed the
    ///     card.
    /// </summary>
    public CardDestination Destination { get; }

    /// <summary>
    ///     Gets the rendered card text. It carries no provenance line of its own: a file gets one line above all the
    ///     cards it holds.
    /// </summary>
    public string Body { get; }
}
