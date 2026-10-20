using SlideRule.Hosting;

namespace SlideRule.Rendering;

/// <summary>Where a layer's local-rules card goes, and what it lists.</summary>
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

    public string LayerName { get; }

    public string? Purpose { get; }

    /// <summary>Gets the Enforce and Migrate rules the layer is the subject of, in model order.</summary>
    public IReadOnlyList<ArchRule> Rules { get; }

    public CardDestination Destination { get; }
}
