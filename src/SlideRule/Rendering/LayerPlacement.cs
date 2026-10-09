using SlideRule.Hosting;

namespace SlideRule.Rendering;

/// <summary>
///     Where a layer's local-rules card goes: the layer's name and purpose, the rules the card lists, and the
///     card's destination.
/// </summary>
internal sealed class LayerPlacement
{
    internal LayerPlacement(
        string layerName, string? purpose, IReadOnlyList<ArchRule> rules, CardDestination destination)
    {
        LayerName = layerName;
        Purpose = purpose;
        Rules = rules;
        Destination = destination;
    }

    /// <summary>
    ///     Gets the layer's name as the spec declares it, <c>Web</c> say.
    /// </summary>
    public string LayerName { get; }

    /// <summary>
    ///     Gets what the layer is for, as the spec states it, carried into the card's lede; null when the layer was
    ///     given no purpose.
    /// </summary>
    public string? Purpose { get; }

    /// <summary>
    ///     Gets the Enforce and Migrate rules the layer is the subject of, in model order, one card bullet each.
    /// </summary>
    public IReadOnlyList<ArchRule> Rules { get; }

    /// <summary>
    ///     Gets the directory whose <c>AGENTS.md</c> receives the layer card, or the reason no directory does.
    /// </summary>
    public CardDestination Destination { get; }
}
