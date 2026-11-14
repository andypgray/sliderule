using System.Text.Json;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     What one <c>global.json</c> says about the SDK: the file, its raw <c>sdk.version</c> and
///     <c>sdk.rollForward</c>, whether it sets <c>sdk.paths</c>, and its <c>sdk.errorMessage</c>. Every value but
///     <see cref="File" /> is null (or false) where the file does not set it, does not parse, or cannot be read.
/// </summary>
internal sealed record GlobalJsonPin(
    string File,
    string? Version,
    string? RollForward,
    bool HasPaths,
    string? ErrorMessage)
{
    private const string FileName = "global.json";

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    ///     The nearest <c>global.json</c> at or above <paramref name="directory" />, the one the .NET host
    ///     resolves an SDK from in that directory, or null when there is none.
    /// </summary>
    internal static GlobalJsonPin? Nearest(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            string candidate = Path.Combine(current.FullName, FileName);
            if (System.IO.File.Exists(candidate)) return Read(candidate);
        }

        return null;
    }

    /// <summary>
    ///     Reads the pin in <paramref name="file" />. Never throws: a file that cannot be read yields a pin that
    ///     names it and nothing else, because the reader runs while a refusal is being worded.
    /// </summary>
    internal static GlobalJsonPin Read(string file)
    {
        try
        {
            return Parse(file, System.IO.File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new GlobalJsonPin(file, null, null, false, null);
        }
    }

    /// <summary>The pin <paramref name="text" /> declares, read as the contents of <paramref name="file" />.</summary>
    // Malformed JSON reads as no pin at all: the SDK itself refuses or ignores such a file, and says so better.
    internal static GlobalJsonPin Parse(string file, string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text, Options);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sdk", out JsonElement sdk)
                || sdk.ValueKind != JsonValueKind.Object)
                return new GlobalJsonPin(file, null, null, false, null);

            return new GlobalJsonPin(
                file,
                StringOf(sdk, "version"),
                StringOf(sdk, "rollForward"),
                sdk.TryGetProperty("paths", out _),
                StringOf(sdk, "errorMessage"));
        }
        catch (JsonException)
        {
            return new GlobalJsonPin(file, null, null, false, null);
        }
    }

    private static string? StringOf(JsonElement sdk, string name)
    {
        return sdk.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
