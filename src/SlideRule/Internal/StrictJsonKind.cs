namespace SlideRule.Internal;

/// <summary>
///     What a value read by <see cref="StrictJson" /> is. The names mirror
///     <c>System.Text.Json.JsonValueKind</c>, so code written against that enum moves across with a type
///     rename.
/// </summary>
/// <remarks>
///     There is no <c>Null</c> and no <c>Undefined</c> member, and neither is an omission. A null literal
///     is refused rather than represented, because the files this reads leave an optional field out
///     instead; and every value a reader can hold came from a text that parsed, so there is no state for
///     "no value" to be in.
/// </remarks>
internal enum StrictJsonKind
{
    /// <summary>An object: zero or more name/value members in file order, every name distinct.</summary>
    Object,

    /// <summary>An array: zero or more values in file order.</summary>
    Array,

    /// <summary>A string, with every escape already resolved.</summary>
    String,

    /// <summary>An integer of any length. A fraction or an exponent is refused rather than read.</summary>
    Number,

    /// <summary>The literal <c>true</c>.</summary>
    True,

    /// <summary>The literal <c>false</c>.</summary>
    False
}
