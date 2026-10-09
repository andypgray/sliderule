namespace SlideRule.Validation;

/// <summary>
///     What several literals decided between them: which mistake it is, the value they collided on, the
///     whole sentence, and the callers' tags for every declaration that took part.
/// </summary>
/// <remarks>
///     A collision names no subject, because the colliding value is already quoted in the sentence, so
///     there is one wording rather than a pair of frames. <see cref="Tags" /> is in list order, so
///     <c>Tags[0]</c> is the declaration the catalog reports at.
/// </remarks>
internal readonly struct CollisionVerdict(
    SpecValidationErrorCode code,
    string key,
    string sentence,
    IReadOnlyList<int> tags)
{
    /// <summary>Which mistake this is.</summary>
    internal SpecValidationErrorCode Code { get; } = code;

    /// <summary>The value that collided: the post-desugar ID, the rule ID, or the layer name.</summary>
    internal string Key { get; } = key;

    /// <summary>The whole message, with no subject clause to fill in.</summary>
    internal string Sentence { get; } = sentence;

    /// <summary>The callers' tags for the declarations that took part, in list order.</summary>
    internal IReadOnlyList<int> Tags { get; } = tags;
}
