using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using Xunit;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over the marker the MCP registry reads to prove this repository owns the server it lists: the
///     landing page carries one <c>mcp-name</c> comment naming the manifest's server, and the tool package ships
///     that page as its readme.
/// </summary>
/// <remarks>
///     The registry validates a published server by fetching the package's readme from nuget.org and looking
///     for the marker there. Nothing else in the build reads it. Without this gate, the first thing to notice
///     a marker gone, renamed or no longer packed is release.yml's wait after publishing, which polls for it
///     for 45 minutes with the packages already live.
/// </remarks>
public sealed class McpNameMarkerTests
{
    /// <summary>The readme's path inside the tool package: the package root, under the landing page's name.</summary>
    private const string PackageReadme = "README.md";

    private const string MarkerKey = "mcp-name:";

    private static readonly Regex MarkerComment =
        new(@"<!--\s*mcp-name:\s*(?<name>\S+?)\s*-->", RegexOptions.CultureInvariant);

    [Fact]
    public void Readme_CarriesOneMcpNameMarker_NamingTheManifestsServer()
    {
        // Arrange
        string readme = RepoRoot.ReadText(DocPaths.RootReadme);
        string serverName = ShouldHaveManifestServerName();

        // Act
        int markers = TextNormalization.Occurrences(readme, MarkerKey);
        Match comment = MarkerComment.Match(readme);

        // Assert
        markers.ShouldBe(
            1,
            $"{DocPaths.RootReadme} must say 'mcp-name:' exactly once, because the registry reads the name "
            + $"that follows it; this run found {markers}.");
        comment.Success.ShouldBeTrue(
            $"The one 'mcp-name:' in {DocPaths.RootReadme} is not a '<!-- mcp-name: <name> -->' comment, so it "
            + "renders on the page instead of staying metadata for the registry.");
        comment.Groups["name"]
            .Value.ShouldBe(
                serverName,
                $"The marker in {DocPaths.RootReadme} must name the server {DocPaths.McpManifest} declares, or the "
                + "registry refuses the listing.");
    }

    [Fact]
    public void CliPackage_PacksTheRootReadmeAsItsReadme()
    {
        // Arrange: parsed as XML rather than matched, because the project's own comment names PackagePath.
        string projectFile = RepoRoot.Absolute(DocPaths.CliProject);
        string projectDirectory = Path.GetDirectoryName(projectFile)!;
        XDocument project = XDocument.Load(projectFile);

        // Act
        XElement[] declared = project.Descendants("PackageReadmeFile")
            .ToArray();
        string[] packedRootReadmes = project.Descendants("None")
            .Where(static item => string.Equals((string?)item.Attribute("Pack"), "true", StringComparison.OrdinalIgnoreCase))
            .Where(item => IncludedPath(projectDirectory, item) == DocPaths.RootReadme)
            .Select(static item => PackedPath(item))
            .ToArray();

        // Assert
        string readmeFile = declared.ShouldHaveSingleItem(
                $"{DocPaths.CliProject} must declare PackageReadmeFile exactly once; this run found {declared.Length}.")
            .Value;
        readmeFile.ShouldBe(
            PackageReadme,
            "The tool package's readme must be the landing page, which carries the marker the registry reads.");
        string packedAt = packedRootReadmes.ShouldHaveSingleItem(
            $"{DocPaths.CliProject} must pack the repository's root {DocPaths.RootReadme} exactly once; this run found "
            + $"{packedRootReadmes.Length} packed items that resolve to it.");
        packedAt.ShouldBe(
            readmeFile,
            "The root README must land at the package root under the name PackageReadmeFile gives, or nuget.org "
            + "serves no readme for the registry to read.");
    }

    private static string ShouldHaveManifestServerName()
    {
        string json = RepoRoot.ReadText(DocPaths.McpManifest);

        using JsonDocument manifest = JsonDocument.Parse(json);
        bool found = manifest.RootElement.TryGetProperty("name", out JsonElement name);
        found.ShouldBeTrue($"{DocPaths.McpManifest} has no name, so there is nothing for the marker to agree with.");

        return name.GetString()
            .ShouldNotBeNull($"{DocPaths.McpManifest} carries no name string.");
    }

    // The repo-relative path an item's Include resolves to from the project's directory. MSBuild accepts
    // either separator on every platform, so the backslashes are made portable before resolving.
    private static string IncludedPath(string projectDirectory, XElement item)
    {
        string include = ((string?)item.Attribute("Include") ?? string.Empty).Replace('\\', '/');
        string resolved = Path.GetFullPath(Path.Combine(projectDirectory, include));

        return RepoRoot.Relative(resolved);
    }

    // Where the item lands inside the package. A PackagePath naming a file with the item's own extension is
    // the whole destination; any other is a folder the item keeps its file name in. An item with no
    // PackagePath goes to the content folders rather than the root, which is spelled out so it cannot match.
    private static string PackedPath(XElement item)
    {
        var packagePath = (string?)item.Attribute("PackagePath");
        if (packagePath is null) return "(content folders)";

        string fileName = Path.GetFileName(((string?)item.Attribute("Include") ?? string.Empty).Replace('\\', '/'));
        string folder = packagePath.Replace('\\', '/')
            .Trim('/');
        if (folder == ".") folder = string.Empty;

        if (Path.GetExtension(folder) == Path.GetExtension(fileName) && folder.Length > 0) return folder;

        return folder.Length == 0 ? fileName : $"{folder}/{fileName}";
    }
}
