namespace SlideRule.Baselines;

/// <summary>Why a baseline file was refused.</summary>
/// <remarks>
///     The arms are parted because a caller wording the refusal for a person says something different
///     for each: a syntax refusal carries a position, a schema one is a sentence about a key, and the two
///     integrity arms each carry their own recovery — one entry's line went wrong, or the one digest
///     covering all of them did.
/// </remarks>
internal enum BaselineRefusalKind
{
    /// <summary>The text is not JSON the strict reader accepts.</summary>
    Syntax,

    /// <summary>The JSON read, and the document is not a baseline: a key missing, unknown, or badly valued.</summary>
    Malformed,

    /// <summary>One entry does not match the seal recorded beside it.</summary>
    SealMismatch,

    /// <summary>A legacy file's whole-file digest does not match the entries it carries.</summary>
    LegacyDigestMismatch
}
