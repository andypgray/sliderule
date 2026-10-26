namespace SlideRule.Internal;

/// <summary>
///     Why <see cref="StrictJson" /> refused a text, and where in it the refusal sits.
/// </summary>
internal sealed class StrictJsonError
{
    internal StrictJsonError(string reason, JsonPosition position)
    {
        Reason = reason;
        Position = position;
    }

    /// <summary>
    ///     The refusal on its own, as a lower-case sentence ending in a full stop. It never names a
    ///     position: <see cref="JsonPosition.Describe" /> over <see cref="Position" /> puts that in front.
    /// </summary>
    internal string Reason { get; }

    internal JsonPosition Position { get; }
}
