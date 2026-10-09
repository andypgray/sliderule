using System.Text.RegularExpressions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate that holds the xunit.v3 requirement the xUnit adapter states to the version of the
///     authoring pair it is built against, and this repository's own adapter test projects to that
///     requirement.
/// </summary>
/// <remarks>
///     <para>
///         The adapter references <c>xunit.v3.extensibility.core</c> and <c>xunit.v3.assert</c>, and the
///         <c>xunit.v3</c> metapackage a test project keeps pins both exactly. The pair's version is
///         therefore the oldest metapackage the adapter runs beside. A test project on an older one restores
///         a mix of the two versions with NU1608 warnings, and nothing here promotes that warning, so the
///         build stays green over the mix.
///     </para>
///     <para>
///         Dependency updates keep the pair current, which moves the requirement without anyone editing the
///         package description or the README that state it to a consumer, or the test projects here that
///         run the adapter the way a consumer does. Each arm holds one of those places to the pair.
///     </para>
/// </remarks>
public sealed class XunitRequirementSyncTests
{
    /// <summary>The metapackage a test project keeps, which pins the authoring pair exactly.</summary>
    private const string Metapackage = "xunit.v3";

    // Whichever place fell behind, the reader weighs the same thing: the pair decides the requirement, and
    // the prose and the test projects follow it.
    private const string Header =
        "The xunit.v3 requirement is out of step with the xUnit adapter's authoring pair. The metapackage "
        + "pins xunit.v3.extensibility.core and xunit.v3.assert exactly, so the version the adapter is built "
        + "against is the oldest metapackage it runs beside, and a test project on an older one restores a mix "
        + "of the two versions with NU1608 warnings that nothing here promotes. When the pair moves, move the "
        + "package description, the README and this repository's adapter test projects with it";

    /// <summary>The library-authoring pair, which a test library references in place of the metapackage.</summary>
    private static readonly string[] AuthoringPair = ["xunit.v3.extensibility.core", "xunit.v3.assert"];

    private static readonly Regex AdapterReference = new(
        @"<(ProjectReference|PackageReference)\s+Include=""[^""]*SlideRule\.Xunit(\.csproj)?""",
        RegexOptions.CultureInvariant);

    /// <summary>How the description and the README word the requirement, with the version it names.</summary>
    private static readonly Regex StatedRequirement =
        new(@"xunit\.v3 (?<version>\d\S*) or later", RegexOptions.CultureInvariant);

    [Fact]
    public void TheAuthoringPair_NamesOneVersion()
    {
        // Arrange
        IReadOnlyList<DeclaredPackage> declared = ProjectPackages.Declared(DocPaths.AdapterProject);
        List<string> findings = new();

        // Act
        findings.AddRange(AuthoringPair
            .Where(package => declared.All(reference => reference.Id != package))
            .Select(package => $"{DocPaths.AdapterProject} declares no {package} at all."));

        List<string> versions = PairReferences(declared)
            .Select(static reference => reference.Version)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (versions.Count > 1)
            findings.Add($"{DocPaths.AdapterProject} declares the pair at {string.Join(" and ", versions)}, "
                         + "which states two requirements at once.");

        // Assert
        findings.ShouldReportNothing(Header);
    }

    [Fact]
    public void TheDescriptionAndReadme_StateThePairsVersion()
    {
        // Arrange
        string pair = PairVersion();
        string description = SolutionInventory.Properties(DocPaths.AdapterProject)
            .GetValueOrDefault("Description") ?? string.Empty;
        List<string> findings = new();

        // Act
        findings.AddRange(RequirementFindings($"{DocPaths.AdapterProject}'s <Description>", description, pair));
        findings.AddRange(RequirementFindings(DocPaths.AdapterReadme, RepoRoot.ReadText(DocPaths.AdapterReadme), pair));

        // Assert
        findings.ShouldReportNothing(Header);
    }

    [Fact]
    public void EveryAdapterTestProject_KeepsTheMetapackageAtOrAboveThePair()
    {
        // Arrange: the projects here that run the adapter the way a consumer does, beside a metapackage of
        // their own. Finding none is a finding, so a move of the adapter project cannot make this pass by
        // leaving nothing to check.
        string pair = PairVersion();
        List<(string Project, string Version)> runners = TrackedFiles.Projects
            .Where(static project => project != DocPaths.AdapterProject
                                     && AdapterReference.IsMatch(RepoRoot.ReadText(project)))
            .SelectMany(static project => ProjectPackages.Declared(project)
                .Where(static reference => reference.Id == Metapackage)
                .Select(reference => (Project: project, reference.Version)))
            .ToList();
        List<string> findings = new();

        // Act
        if (runners.Count == 0)
            findings.Add($"No tracked project references the adapter beside a {Metapackage} metapackage, so "
                         + "nothing here runs it the way a consumer does.");

        findings.AddRange(runners
            .Where(runner => !IsAtOrAbove(runner.Version, pair))
            .Select(runner => $"{runner.Project} keeps {Metapackage} {runner.Version}, below the {pair} the "
                              + "adapter is built against."));

        // Assert
        findings.ShouldReportNothing(Header);
    }

    // The adapter's references to either package of the pair, in file order.
    private static IEnumerable<DeclaredPackage> PairReferences(IReadOnlyList<DeclaredPackage> declared)
    {
        return declared.Where(static reference => AuthoringPair.Contains(reference.Id, StringComparer.Ordinal));
    }

    // The version the pair is built against, read from its first reference; the arm that holds the pair to
    // one version covers the second. Empty when the adapter declares neither.
    private static string PairVersion()
    {
        return PairReferences(ProjectPackages.Declared(DocPaths.AdapterProject))
            .Select(static reference => reference.Version)
            .FirstOrDefault() ?? "";
    }

    // Each place a text states the requirement at a version other than the pair's, or that it states none.
    private static IEnumerable<string> RequirementFindings(string where, string text, string pair)
    {
        List<string> stated = StatedRequirement.Matches(text)
            .Select(static match => match.Groups["version"].Value)
            .ToList();
        if (stated.Count == 0) return [$"{where} states no \"{Metapackage} <version> or later\" requirement."];

        return stated
            .Where(version => version != pair)
            .Select(version => $"{where} states {Metapackage} {version} or later, but the adapter is built "
                               + $"against {pair}.");
    }

    // Whether a metapackage version meets the pair's, compared as numeric versions. One that does not parse
    // as a numeric version, a prerelease suffix among them, is not at or above anything, so it reports
    // rather than passes.
    private static bool IsAtOrAbove(string version, string pair)
    {
        return Version.TryParse(version, out Version? kept)
               && Version.TryParse(pair, out Version? required)
               && kept >= required;
    }
}
