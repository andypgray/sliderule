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
    ///     position — <see cref="Describe" /> puts that in front.
    /// </summary>
    internal string Reason { get; }

    /// <summary>Where the refusal sits in the text that was parsed.</summary>
    internal JsonPosition Position { get; }

    /// <summary>The refusal with its position in front: <c>line 2, column 8: ...</c>.</summary>
    internal string Describe()
    {
        return Position.Describe(Reason);
    }
}
