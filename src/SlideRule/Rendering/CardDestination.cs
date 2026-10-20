namespace SlideRule.Rendering;

/// <summary>
///     Where one context card goes: the directory whose <c>AGENTS.md</c> receives it, or no directory and the
///     reason the card was skipped. Exactly one of the two is set.
/// </summary>
internal sealed class CardDestination
{
    private CardDestination(string? directoryPath, string? skipReason)
    {
        DirectoryPath = directoryPath;
        SkipReason = skipReason;
    }

    /// <summary>
    ///     Gets the directory whose <c>AGENTS.md</c> receives the card, placed by <see cref="DirectoryPlacement" />;
    ///     null when the layer or scope matched no type in the solution, or only types declared outside the
    ///     solution directory.
    /// </summary>
    public string? DirectoryPath { get; }

    public string? SkipReason { get; }

    internal static CardDestination Into(string directoryPath)
    {
        return new CardDestination(directoryPath, null);
    }

    internal static CardDestination Skipped(string reason)
    {
        return new CardDestination(null, reason);
    }
}
