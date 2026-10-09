using System.Text.Json;
using SlideRule.Roslyn;

namespace SlideRule.Cli.Settings;

/// <summary>
///     Reads a <c>sliderule.json</c>. Comments and trailing commas are allowed, since the file is written by
///     hand; anything else the file says that this reader does not know is refused by name, because a key
///     misspelt in a committed file would otherwise be ignored on every run with nothing to say so.
/// </summary>
internal static class ProjectSettingsReader
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // The editor's schema pointer is the one key read and ignored: it is for the editor, and refusing it would
    // make a file that validates in the editor fail here.
    private const string SchemaKey = "$schema";

    private const string RenderSection = "render.";

    private const string DiagramSection = "render.diagram.";

    /// <summary>Reads and validates the settings file at <paramref name="path" />.</summary>
    /// <param name="path">The settings file; its directory is what every path in it is relative to.</param>
    /// <exception cref="UserErrorException">The file cannot be read, is not JSON, or says something this does not take.</exception>
    public static ProjectSettings Read(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string text = ReadText(fullPath);

        using JsonDocument document = Parse(fullPath, text);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw Refusal(fullPath, "the settings are one JSON object, and this file holds something else.");

        RefuseUnknownKeys(fullPath, root, "", [SchemaKey, "solution", "spec", "render"]);

        string directory = Path.GetDirectoryName(fullPath)!;
        string solution = RequiredString(
            fullPath, root, "", "solution", "name the solution file these settings are for, relative to this file");
        string? spec = OptionalString(fullPath, root, "spec");
        RenderSettings render = root.TryGetProperty("render", out JsonElement renderElement)
            ? ReadRender(fullPath, renderElement, directory)
            : RenderSettings.None;

        return new ProjectSettings(
            fullPath,
            Path.GetFullPath(solution, directory),
            spec is null ? null : Path.GetFullPath(spec, directory),
            render);
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UserErrorException($"{path}: could not be read: {ex.Message}", ex);
        }
    }

    private static JsonDocument Parse(string path, string text)
    {
        try
        {
            return JsonDocument.Parse(text, Options);
        }
        catch (JsonException ex)
        {
            throw new UserErrorException($"{path}: not valid JSON: {ex.Message}", ex);
        }
    }

    private static RenderSettings ReadRender(string path, JsonElement render, string directory)
    {
        if (render.ValueKind != JsonValueKind.Object)
            throw Refusal(path, "'render' is an object naming the files render writes: model, diagram.");

        RefuseUnknownKeys(path, render, RenderSection, ["model", "diagram"]);

        bool model = OptionalBool(path, render, RenderSection, "model");
        DiagramSettings? diagram = render.TryGetProperty("diagram", out JsonElement diagramElement)
            ? ReadDiagram(path, diagramElement, directory)
            : null;

        return new RenderSettings(model, diagram);
    }

    private static DiagramSettings ReadDiagram(string path, JsonElement diagram, string directory)
    {
        if (diagram.ValueKind != JsonValueKind.Object)
            throw Refusal(path, "'render.diagram' is an object: path, and optionally only and exclude.");

        RefuseUnknownKeys(path, diagram, DiagramSection, ["path", "only", "exclude"]);

        string target = RequiredString(
            path, diagram, DiagramSection, "path", "name the file the drawings go into, relative to this file");

        return new DiagramSettings(
            Path.GetFullPath(target, directory),
            StringList(path, diagram, DiagramSection, "only"),
            StringList(path, diagram, DiagramSection, "exclude"));
    }

    private static void RefuseUnknownKeys(string path, JsonElement element, string prefix, IReadOnlyList<string> known)
    {
        string? unknown = element.EnumerateObject()
            .Select(static property => property.Name)
            .FirstOrDefault(name => !known.Contains(name, StringComparer.Ordinal));
        if (unknown is null) return;

        IEnumerable<string> offered = known.Where(static key => key != SchemaKey);
        throw Refusal(path, $"unknown key '{prefix}{unknown}'; {Where(prefix)} takes {string.Join(", ", offered)}.");
    }

    private static string Where(string prefix)
    {
        return prefix.Length == 0 ? "the file" : $"'{prefix.TrimEnd('.')}'";
    }

    private static string RequiredString(string path, JsonElement element, string prefix, string key, string purpose)
    {
        string label = prefix + key;
        if (!element.TryGetProperty(key, out JsonElement value))
            throw Refusal(path, $"'{label}' is required: {purpose}.");

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Refusal(path, $"'{label}' must be a non-blank string: {purpose}.");

        return value.GetString()!;
    }

    private static string? OptionalString(string path, JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Refusal(path, $"'{key}' must be a non-blank string, or left out for the convention.");

        return value.GetString();
    }

    private static bool OptionalBool(string path, JsonElement element, string prefix, string key)
    {
        if (!element.TryGetProperty(key, out JsonElement value)) return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Refusal(path, $"'{prefix}{key}' must be true or false.")
        };
    }

    private static IReadOnlyList<string> StringList(string path, JsonElement element, string prefix, string key)
    {
        if (!element.TryGetProperty(key, out JsonElement value)) return [];

        if (value.ValueKind != JsonValueKind.Array
            || value.EnumerateArray().Any(static item => item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())))
            throw Refusal(path, $"'{prefix}{key}' must be an array of non-blank project-name globs.");

        return value.EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToList();
    }

    private static UserErrorException Refusal(string path, string message)
    {
        return new UserErrorException($"{path}: {message}");
    }
}
