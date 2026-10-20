namespace SlideRule.Baselines;

/// <summary>Why a baseline file was refused.</summary>
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
