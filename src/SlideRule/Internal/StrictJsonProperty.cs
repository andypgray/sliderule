namespace SlideRule.Internal;

/// <summary>
///     One member of a JSON object: its name, its value, and where the name sits in the text.
/// </summary>
/// <remarks>
///     The position is the opening quote of the <em>name</em>, not of the value —
///     <see cref="StrictJsonValue.Position" /> on <see cref="Value" /> gives the other one. A caller
///     reporting a bad field points at the name, because that is the word the reader is looking for.
/// </remarks>
internal readonly struct StrictJsonProperty
{
    internal StrictJsonProperty(string name, StrictJsonValue value, JsonPosition position)
    {
        Name = name;
        Value = value;
        Position = position;
    }

    /// <summary>The member's name, with every escape already resolved.</summary>
    internal string Name { get; }

    /// <summary>The member's value.</summary>
    internal StrictJsonValue Value { get; }

    /// <summary>Where the name's opening quote sits in the text that was parsed.</summary>
    internal JsonPosition Position { get; }
}
