namespace SlideRule.Internal;

/// <summary>
///     What "single-line" means for every authored prose value the product carries, such as a spec trailer or
///     a baseline attribution.
/// </summary>
/// <remarks>Blankness is not folded in: the spec catalog reports blank under its own code.</remarks>
internal static class SingleLineProse
{
    internal static bool IsMultiLine(string value)
    {
        return value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
    }
}
