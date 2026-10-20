namespace SlideRule.ModelFile;

/// <summary>Why a model file was refused.</summary>
internal enum ModelRefusalKind
{
    /// <summary>The text is not a model file: bad JSON, a missing or unexpected key, a bad value.</summary>
    Malformed,

    /// <summary>The file declares a schema version above this reader's: a reason to update the tool.</summary>
    Newer,

    /// <summary>The file declares a schema version below this reader's: a reason to rewrite the file.</summary>
    Older
}
