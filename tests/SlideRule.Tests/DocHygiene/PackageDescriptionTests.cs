using System.Text.RegularExpressions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gates over the description every shipped package carries: the text nuget.org puts in its search
///     results and a package manager shows beside the package's name. Every package declares one, and each
///     library package's description opens with the first sentence of the readme it ships, which nuget.org shows
///     as the package's page.
/// </summary>
/// <remarks>
///     The tool's description is held by <see cref="LedeSyncTests" /> instead, because the readme it ships is the
///     landing page and that page's first sentence is the lede.
/// </remarks>
public sealed class PackageDescriptionTests
{
    private const string DeclaredHeader =
        "A shipped package declares no description. nuget.org search results and package managers show the "
        + "description beside the package's name, and without one they show the SDK's placeholder";

    private const string LedeHeader =
        "A library package's description no longer opens with its readme's first sentence. The readme is the "
        + "package's page on nuget.org and owns the sentence, so edit it there and carry the description with it";

    /// <summary>Where one sentence of a plain-text paragraph ends and the next begins.</summary>
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+(?=[A-Z""(])");

    [Fact]
    public void EveryPackage_DeclaresADescription()
    {
        // Arrange
        IReadOnlyList<SolutionProject> packages = SolutionInventory.PackableProjects;
        List<string> findings = new();

        // Act
        if (packages.Count == 0) findings.Add("The solution declares no project that packs, so no description was read.");

        IEnumerable<string> undescribed = packages
            .Where(static package => DescriptionOf(package.CsprojPath).Length == 0)
            .Select(static package => $"{package.CsprojPath} declares no <Description>.");
        findings.AddRange(undescribed);

        // Assert
        findings.ShouldReportNothing(DeclaredHeader);
    }

    [Fact]
    public void EveryLibraryPackageDescription_OpensWithItsReadmeLede()
    {
        // Arrange
        List<SolutionProject> libraries = SolutionInventory.PackableProjects
            .Where(static package => !package.IsTool)
            .ToList();
        List<string> findings = new();

        // Act
        if (libraries.Count == 0) findings.Add("The solution declares no library package, so no description was read.");

        foreach (SolutionProject library in libraries)
        {
            string readme = ReadmeOf(library);
            if (readme.Length == 0 || !File.Exists(RepoRoot.Absolute(readme)))
            {
                findings.Add($"{library.CsprojPath} ships no readme beside it for its description to open with.");
                continue;
            }

            string lede = FirstSentence(readme);
            string description = DescriptionOf(library.CsprojPath);
            if (!description.StartsWith(lede, StringComparison.Ordinal))
                findings.Add($"{library.CsprojPath}'s <Description> should open with the first sentence of {readme}, "
                             + $"\"{lede}\". It reads \"{description}\".");
        }

        // Assert
        findings.ShouldReportNothing(LedeHeader);
    }

    /// <summary>The <c>Description</c> the project file at <paramref name="csprojPath" /> declares, or empty.</summary>
    internal static string DescriptionOf(string csprojPath)
    {
        return SolutionInventory.Properties(csprojPath)
            .GetValueOrDefault("Description") ?? string.Empty;
    }

    // The readme a package ships: the file its PackageReadmeFile names, beside its project file. Empty where the
    // project names none.
    private static string ReadmeOf(SolutionProject package)
    {
        string? file = SolutionInventory.Properties(package.CsprojPath)
            .GetValueOrDefault("PackageReadmeFile");
        if (string.IsNullOrEmpty(file)) return string.Empty;

        string directory = package.CsprojPath[..(package.CsprojPath.LastIndexOf('/') + 1)];

        return directory + file;
    }

    // The first sentence of the readme's first paragraph under its title, as the words a reader sees.
    private static string FirstSentence(string readme)
    {
        IEnumerable<string> paragraph = RepoRoot.ReadLines(readme)
            .SkipWhile(static line => string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            .TakeWhile(static line => !string.IsNullOrWhiteSpace(line));
        string text = DocProse.PlainText(string.Join(" ", paragraph));

        return SentenceBreak.Split(text, 2)[0];
    }
}
