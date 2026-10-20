namespace SlideRule.Internal;

/// <summary>One member of a JSON object: its name, its value, and where the name sits in the text.</summary>
internal readonly struct StrictJsonProperty
{
    internal StrictJsonProperty(string name, StrictJsonValue value, JsonPosition position)
    {
        Name = name;
        Value = value;
        Position = position;
    }

    internal string Name { get; }

    internal StrictJsonValue Value { get; }

    /// <summary>
    ///     Where the name's opening quote sits, not the value's (<see cref="StrictJsonValue.Position" /> gives
    ///     that): a bad field is reported at its name, the word the reader is looking for.
    /// </summary>
    internal JsonPosition Position { get; }
}
