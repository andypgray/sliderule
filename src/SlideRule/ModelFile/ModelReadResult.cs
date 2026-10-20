namespace SlideRule.ModelFile;

/// <summary>The outcome of reading a model file: exactly one of a document and a refusal.</summary>
/// <remarks>
///     Two nullable slots rather than a discriminated pair, because netstandard2.0 has neither, and the
///     invariant is held by the two factories below being the only way to make one.
/// </remarks>
internal sealed class ModelReadResult
{
    private ModelReadResult(ModelDocument? document, ModelRefusal? refusal)
    {
        Document = document;
        Refusal = refusal;
    }

    internal ModelDocument? Document { get; }

    internal ModelRefusal? Refusal { get; }

    internal static ModelReadResult Read(ModelDocument document)
    {
        return new ModelReadResult(document, null);
    }

    internal static ModelReadResult Refused(ModelRefusal refusal)
    {
        return new ModelReadResult(null, refusal);
    }
}
