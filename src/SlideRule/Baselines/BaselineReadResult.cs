namespace SlideRule.Baselines;

/// <summary>The outcome of reading a baseline file: exactly one of a document and a refusal.</summary>
internal sealed class BaselineReadResult
{
    private BaselineReadResult(BaselineDocument? document, BaselineRefusal? refusal)
    {
        Document = document;
        Refusal = refusal;
    }

    internal BaselineDocument? Document { get; }

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
