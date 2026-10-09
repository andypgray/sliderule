namespace SlideRule.Baselines;

/// <summary>The outcome of reading a baseline file: exactly one of a document and a refusal.</summary>
/// <remarks>
///     Two nullable slots rather than a discriminated pair, because netstandard2.0 has neither, and the
///     invariant is held by the two factories below being the only way to make one.
/// </remarks>
internal sealed class BaselineReadResult
{
    private BaselineReadResult(BaselineDocument? document, BaselineRefusal? refusal)
    {
        Document = document;
        Refusal = refusal;
    }

    /// <summary>The document the text carried, or null when the text was refused.</summary>
    internal BaselineDocument? Document { get; }

    /// <summary>Why the text was refused, or null when it read.</summary>
    internal BaselineRefusal? Refusal { get; }

    internal static BaselineReadResult Read(BaselineDocument document)
    {
        return new BaselineReadResult(document, null);
    }

    internal static BaselineReadResult Refused(BaselineRefusal refusal)
    {
        return new BaselineReadResult(null, refusal);
    }
}
