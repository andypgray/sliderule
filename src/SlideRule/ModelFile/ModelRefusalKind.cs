namespace SlideRule.ModelFile;

/// <summary>Why a model file was refused.</summary>
/// <remarks>
///     The version arms are parted from <see cref="Malformed" /> because they mean something a caller can
///     act on: a file this reader is too old for is a reason to update the tool, and one it is too new
///     for is a reason to rewrite the file. A malformed file is neither — it is simply wrong.
/// </remarks>
internal enum ModelRefusalKind
{
    /// <summary>The text is not a model file: bad JSON, a missing or unexpected key, a bad value.</summary>
    Malformed,

    /// <summary>The file declares a schema version above the one this reader understands.</summary>
    Newer,

    /// <summary>The file declares a schema version below the one this reader understands.</summary>
    Older
}
