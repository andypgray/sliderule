namespace SlideRule.Model;

/// <summary>
///     Mutable backing state for a declared layer. <c>Purpose</c> appends to a list, so a repeat is detectable
///     by validation (§8 item 6).
/// </summary>
/// <remarks>
///     Not a <see cref="Registration" />: a layer has no ID and captures no spec-source location, which is
///     why every layer error is spec-wide and named by layer.
/// </remarks>
internal sealed class LayerRegistration(LayerNoun noun)
{
    internal LayerNoun Noun { get; } = noun;

    internal List<string> Purposes { get; } = [];
}
