using System.Text.Json;
using System.Xml.Linq;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate that keeps every place this repository states its own version naming one version.
/// </summary>
/// <remarks>
///     <para>
///         It holds all four sites to the props file's version in the suite, before any tag exists, and
///         names the site that disagrees. release.yml compares every site but the SARIF golden with the
///         tag, after the tag is pushed.
///     </para>
///     <para>
///         Besides this gate, the SARIF golden's driver version is held only by whole-document golden
///         comparisons, where a missed bump would read as a rendering regression.
///     </para>
///     <para>
///         The props file is parsed as XML rather than matched, because its own comment carries the literal
///         <c>"&lt;Version&gt;"</c>, and an element query cannot see inside a comment.
///     </para>
/// </remarks>
public sealed class ManifestVersionTests
{
    private const string SarifGoldenPath = "tests/SlideRule.Tests/Cli/Golden/violated-check.sarif";

    private const string ManifestTopSite = $"{DocPaths.McpManifest} .version";
    private const string ManifestPackageSite = $"{DocPaths.McpManifest} .packages[0].version";
    private const string SarifDriverSite = $"{SarifGoldenPath} runs[0].tool.driver.version";

    [Fact]
    public void EveryVersionSite_NamesTheSameVersion()
    {
        // Arrange
        IReadOnlyList<VersionSite> sites = ShouldReadSites();

        // Act
        string[] distinct = sites
            .Select(static site => site.Version)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Assert
        distinct.ShouldHaveSingleItem(
            "A version bump has to reach all four of these, and one that reaches only some of them "
            + "fails a long way from the edit: the two manifest fields are refused against the tag "
            + "inside release.yml, with the release already running, and the SARIF golden reds as a "
            + $"document diff at the end of a Release suite. What the sites say:\n{Describe(sites)}");
    }

    private static IReadOnlyList<VersionSite> ShouldReadSites()
    {
        (string top, string package) = ShouldHaveManifestVersions();

        return
        [
            new VersionSite($"{SolutionInventory.RootProps} <Version>", ShouldHaveSinglePropsVersion()),
            new VersionSite(ManifestTopSite, top),
            new VersionSite(ManifestPackageSite, package),
            new VersionSite(SarifDriverSite, ShouldHaveSarifDriverVersion())
        ];
    }

    private static string ShouldHaveSinglePropsVersion()
    {
        XDocument props = XDocument.Load(RepoRoot.Absolute(SolutionInventory.RootProps));

        XElement[] declared = props.Descendants("Version")
            .ToArray();
        XElement version = declared.ShouldHaveSingleItem(
            $"{SolutionInventory.RootProps} declares the lockstep <Version> exactly once, for every lockstep package at "
            + $"once; this run found {declared.Length}.");

        return version.Value;
    }

    private static (string Top, string Package) ShouldHaveManifestVersions()
    {
        string json = RepoRoot.ReadText(DocPaths.McpManifest);

        using JsonDocument manifest = JsonDocument.Parse(json);
        JsonElement root = manifest.RootElement;
        JsonElement firstPackage = root.GetProperty("packages")[0];

        string top = ShouldHaveText(root, "version", ManifestTopSite);
        string package = ShouldHaveText(firstPackage, "version", ManifestPackageSite);

        return (top, package);
    }

    // The golden's shape is CheckCommandE2ETests' pin, so navigating it plainly is safe; only the
    // leaf — the field a bump has to reach and nothing cross-checks against the tag — is read here.
    private static string ShouldHaveSarifDriverVersion()
    {
        string json = RepoRoot.ReadText(SarifGoldenPath);

        using JsonDocument golden = JsonDocument.Parse(json);
        JsonElement firstRun = golden.RootElement.GetProperty("runs")[0];
        JsonElement driver = firstRun.GetProperty("tool")
            .GetProperty("driver");

        return ShouldHaveText(driver, "version", SarifDriverSite);
    }

    private static string ShouldHaveText(JsonElement parent, string name, string site)
    {
        return parent.StringOrDefault(name)
            .ShouldNotBeNull($"{site} carries no version string, so nothing there states a version at all.");
    }

    private static string Describe(IEnumerable<VersionSite> sites)
    {
        IEnumerable<string> lines = sites.Select(static site => $"  {site.Site} -> {site.Version}");

        return string.Join("\n", lines);
    }

    private sealed record VersionSite(string Site, string Version);
}
