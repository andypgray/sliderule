using System.Text.Json;
using Xunit;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over the descriptions of the MCP registry manifest's package arguments, runtime arguments and
///     environment variables: the text a registry client shows the person supplying each value at install time. Each
///     reads as plain declaratives, held the way a package description is.
/// </summary>
/// <remarks>
///     The manifest's own <c>description</c> is held by <see cref="LedeSyncTests" /> instead, because it is the
///     README lede without its subject.
/// </remarks>
public sealed class McpManifestDescriptionTests
{
    private const string Header =
        "A registry manifest input description departs from the house register. A registry client shows it on its "
        + "own, to the person supplying the value at install time, so each is held the way a package description "
        + "is: no sentence over 25 words, no em-dash, at most one colon mid-sentence, at most one contrast frame, "
        + "and no stock phrase, off-voice word or internal reference";

    // The input arrays of a package entry that carry descriptions: the registry schema gives both argument arrays
    // the same shape, and the variables a shape of their own.
    private static readonly string[] InputArrays = ["packageArguments", "runtimeArguments", "environmentVariables"];

    // The arrays the manifest describes today, each guarded on its own, so a renamed array reds rather than going
    // unread while the other is still read.
    private static readonly string[] DescribedArrays = ["packageArguments", "environmentVariables"];

    [Fact]
    public void EveryInputDescription_ReadsAsPlainDeclaratives()
    {
        // Arrange
        List<(string Array, string Label, string Text)> descriptions = InputDescriptions();
        List<string> findings = new();

        // Act
        IEnumerable<string> unread = DescribedArrays
            .Where(array => !descriptions.Any(description => description.Array == array))
            .Select(static array =>
                $"{DocPaths.McpManifest} yielded no description from any package's {array}, so none was read.");
        findings.AddRange(unread);

        foreach ((string _, string label, string text) in descriptions)
            findings.AddRange(StandaloneTextShape.Findings(text)
                .Select(departure => $"{DocPaths.McpManifest} {label}: {departure}"));

        // Assert
        findings.ShouldReportNothing(Header);
    }

    // Every input that carries a description, labelled with the path to it and the name a reader knows it by.
    private static List<(string Array, string Label, string Text)> InputDescriptions()
    {
        using JsonDocument manifest = JsonDocument.Parse(RepoRoot.ReadText(DocPaths.McpManifest));
        List<(string Array, string Label, string Text)> descriptions = new();
        if (!manifest.RootElement.TryGetProperty("packages", out JsonElement packages)) return descriptions;

        var i = 0;
        foreach (JsonElement package in packages.EnumerateArray())
        {
            foreach (string array in InputArrays)
            {
                if (!package.TryGetProperty(array, out JsonElement inputs)) continue;

                var j = 0;
                foreach (JsonElement input in inputs.EnumerateArray())
                {
                    if (StringOf(input, "description") is { } text)
                    {
                        string name = StringOf(input, "name") ?? StringOf(input, "valueHint") ?? StringOf(input, "value") ?? "";
                        descriptions.Add((array, $".packages[{i}].{array}[{j}] ({name})", text));
                    }

                    j++;
                }
            }

            i++;
        }

        return descriptions;
    }

    private static string? StringOf(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;
    }
}
