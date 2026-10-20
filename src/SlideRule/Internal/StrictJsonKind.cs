namespace SlideRule.Internal;

/// <summary>
///     What a value read by <see cref="StrictJson" /> is. The names mirror
///     <c>System.Text.Json.JsonValueKind</c>, so code written against that enum moves across with a type
///     rename.
/// </summary>
/// <remarks>
///     There is no <c>Null</c> member, since a null literal is refused rather than represented, and no
///     <c>Undefined</c>, since every value a reader can hold came from a text that parsed.
/// </remarks>
internal enum StrictJsonKind
{
    Object,
    Array,
    String,

    /// <summary>An integer of any length.</summary>
    Number,

    True,
    False
}
