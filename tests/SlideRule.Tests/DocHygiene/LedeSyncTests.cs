using System.Text.Json;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Holds every copy of the landing page's lede to the README, which owns the sentence. The root command's
///     description, the line <c>sliderule --help</c> opens with, is the lede. The CLI package's description
///     (the first text nuget.org shows for the tool) and the contributor guide open with it, and the contract
///     package's readme introduces the product with it. The MCP registry manifest's description is the lede
///     without its subject, because the registry caps that field at 100 characters and the whole sentence runs
///     past the cap.
/// </summary>
public sealed class LedeSyncTests
{
    private const string LedeOpening = "SlideRule is ";

    /// <summary>The longest server description the MCP registry's schema accepts.</summary>
    private const int RegistryDescriptionCap = 100;

    // One edit to the lede moves every copy at once, so the red lists every copy left behind.
    private const string Header =
        "A copy of the README lede is out of step with it. The README owns the sentence, so edit it there and "
        + "carry each copy below with it";

    [Fact]
    public void EveryCopy_CarriesTheReadmeLede()
    {
        // Arrange
        string lede = RepoRoot.ReadLines(DocPaths.RootReadme)
            .First(static line => line.StartsWith(LedeOpening, StringComparison.Ordinal));
        string predicate = WithoutSubject(lede);
        List<string> findings = new();

        // Act
        string rootDescription = CommandTree.Root()
            .Description ?? string.Empty;
        if (rootDescription != lede)
            findings.Add("The root command's description, the first line of sliderule --help, reads "
                         + $"\"{rootDescription}\". It should be the lede.");

        string packageDescription = SolutionInventory.Properties(DocPaths.CliProject)
            .GetValueOrDefault("Description") ?? string.Empty;
        if (!packageDescription.StartsWith(lede, StringComparison.Ordinal))
            findings.Add($"{DocPaths.CliProject}'s <Description> should open with the lede. It reads "
                         + $"\"{packageDescription}\".");

        string contributingOpening = FirstProseLine(DocPaths.Contributing);
        if (!contributingOpening.StartsWith(lede, StringComparison.Ordinal))
            findings.Add($"{DocPaths.Contributing} should open with the lede. Its first line of prose reads "
                         + $"\"{contributingOpening}\".");

        // Hard-wrapped, with a link on the product's name, so it is compared as the words a reader sees.
        string contractReadme = DocProse.PlainText(RepoRoot.ReadText(DocPaths.ContractReadme));
        if (!contractReadme.Contains(lede, StringComparison.Ordinal))
            findings.Add($"{DocPaths.ContractReadme} should introduce the product with the lede, and no sentence in "
                         + "it reads that way.");

        string manifestDescription = ManifestDescription();
        if (manifestDescription != predicate)
            findings.Add($"{DocPaths.McpManifest}'s description reads \"{manifestDescription}\". It should be "
                         + $"the lede without its subject: \"{predicate}\".");

        if (predicate.Length > RegistryDescriptionCap)
            findings.Add($"The lede without its subject runs to {predicate.Length} characters, and the MCP "
                         + $"registry refuses a description longer than {RegistryDescriptionCap}. Shorten the "
                         + $"lede, or give {DocPaths.McpManifest} a text of its own and hold that here instead.");

        // Assert
        findings.ShouldReportNothing(Header);
    }

    // "SlideRule is a .NET architecture checker ..." becomes "A .NET architecture checker ...".
    private static string WithoutSubject(string lede)
    {
        string predicate = lede[LedeOpening.Length..];

        return char.ToUpperInvariant(predicate[0]) + predicate[1..];
    }

    // The first line a reader meets under the title: neither blank nor a heading.
    private static string FirstProseLine(string doc)
    {
        return RepoRoot.ReadLines(doc)
            .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#')) ?? string.Empty;
    }

    private static string ManifestDescription()
    {
        using JsonDocument manifest = JsonDocument.Parse(RepoRoot.ReadText(DocPaths.McpManifest));

        return manifest.RootElement.TryGetProperty("description", out JsonElement description)
            ? description.GetString() ?? string.Empty
            : string.Empty;
    }
}
