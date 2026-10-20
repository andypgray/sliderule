namespace SlideRule.Validation;

/// <summary>What several literals decided between them.</summary>
internal readonly struct CollisionVerdict(
    SpecValidationErrorCode code,
    string key,
    string sentence,
    IReadOnlyList<int> tags)
{
    internal SpecValidationErrorCode Code { get; } = code;

    /// <summary>The value that collided: the post-desugar ID, the rule ID, or the layer name.</summary>
    internal string Key { get; } = key;

    /// <summary>
    ///     The whole message. It quotes the colliding value and names no subject, so unlike a literal's verdict
    ///     it has one wording rather than a pair of frames.
    /// </summary>
    internal string Sentence { get; } = sentence;

    /// <summary>
    ///     The callers' tags for the declarations that took part, in list order: <c>Tags[0]</c> is the
    ///     declaration the catalog reports at.
    /// </summary>
    internal IReadOnlyList<int> Tags { get; } = tags;
}
