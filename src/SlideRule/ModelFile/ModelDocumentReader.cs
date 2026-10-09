using SlideRule.Internal;

namespace SlideRule.ModelFile;

/// <summary>Reads a model file's text back into a document, or says why it is not one.</summary>
/// <remarks>
///     Nothing here throws for content, whatever the text says: a file that is truncated, hand-edited
///     into nonsense, or written by a different version of this format comes back as a refusal naming
///     what is wrong and where. A null text is a different thing — a caller bug — and throws.
/// </remarks>
internal static class ModelDocumentReader
{
    internal static ModelReadResult Read(string text)
    {
        Guard.NotNull(text, nameof(text));

        if (!StrictJson.TryParse(text, out StrictJsonValue? root, out StrictJsonError? error))
        {
            // The JSON reader's refusal passes through with its own words and position: it already says
            // what is wrong in the same register, and re-wording it would only move the reader's eye.
            StrictJsonError json = error!;
            return ModelReadResult.Refused(
                new ModelRefusal(ModelRefusalKind.Malformed, json.Reason, json.Position));
        }

        var reader = new ModelNodeReader();
        ModelDocument? document = reader.ReadDocument(root!);
        if (reader.Refusal is { } refusal) return ModelReadResult.Refused(refusal);

        return ModelReadResult.Read(document!);
    }
}
