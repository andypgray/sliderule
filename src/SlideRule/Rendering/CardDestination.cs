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
    ///     Gets the directory whose <c>AGENTS.md</c> receives the card, which is the deepest one holding every file
    ///     inside the solution directory that declares one of the card's types. Null when the layer or scope matched
    ///     no type in the solution, or only types declared outside the solution directory; then
    ///     <see cref="SkipReason" /> says which.
    /// </summary>
    public string? DirectoryPath { get; }

    /// <summary>
    ///     Gets why no directory was resolved, set exactly when <see cref="DirectoryPath" /> is null. A host with
    ///     somewhere to put it prints it as a warning.
    /// </summary>
    public string? SkipReason { get; }

    /// <summary>A card placed in <paramref name="directoryPath" />.</summary>
    internal static CardDestination Into(string directoryPath)
    {
        return new CardDestination(directoryPath, null);
    }

    /// <summary>A card no directory receives, for the reason given.</summary>
    internal static CardDestination Skipped(string reason)
    {
        return new CardDestination(null, reason);
    }
}
