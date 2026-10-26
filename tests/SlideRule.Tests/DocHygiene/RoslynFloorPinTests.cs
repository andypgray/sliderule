using System.Text.Json;
using System.Text.RegularExpressions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over the compiler-API floor: the oldest <c>Microsoft.CodeAnalysis</c> version SlideRule
///     runs against, stated in each project file that declares the compiler API and held there against
///     automated dependency updates by an ignore on every nuget entry of the Dependabot configuration.
/// </summary>
/// <remarks>
///     <para>
///         A floor is not a dependency to keep fresh. Every other package reference in this
///         repository wants to be current, and the update PRs that keep them so are the point of the
///         Dependabot configuration. This one is the opposite: the version is a claim about which hosts
///         the extraction package still loads inside, so moving it up is a support decision with a
///         release note, not a maintenance chore.
///     </para>
///     <para>
///         Both halves are held, because either alone is silent. The version in the project file is what
///         the package actually resolves against; the ignore entry is what stops a weekly update run
///         from raising it. Without the ignore the floor moves in a PR that looks like every other
///         bump — green build, green suite, nothing on the page saying what was given up. Without the
///         pinned literal the ignore protects a version nobody states.
///     </para>
///     <para>
///         The ignore is held on every nuget entry rather than on one that lists a floor project, because
///         an update run bumps packages in every project its listed projects reach through a
///         <c>ProjectReference</c>: the run for the tests project reaches all three projects that state the
///         floor, so an ignore on a floor project's own entry does not hold there. Each package is ignored
///         by its exact name, and the ignored names are held to the floor declarations alone, because a name
///         that also matched a reference meant to stay current would freeze that reference silently.
///     </para>
/// </remarks>
public sealed class RoslynFloorPinTests
{
    private const string ExtractionCsproj = "src/SlideRule.Extraction/SlideRule.Extraction.csproj";

    private const string AnalyzerLockFile = "src/SlideRule.Analyzers/packages.lock.json";

    private const string SpecAnalyzerCsproj =
        "src/SlideRule.SpecAnalyzers/SlideRule.SpecAnalyzers.csproj";

    private const string SpecCompletionCsproj =
        "src/SlideRule.SpecCompletion/SlideRule.SpecCompletion.csproj";

    private const string DependabotConfig = DependabotEntries.ConfigPath;

    private const string FloorVersion = "4.0.1";

    /// <summary>How a lock file spells an entry the graph reached through another package's reference.</summary>
    private const string TransitiveEntry = "Transitive";

    /// <summary>
    ///     The reference up to its version, for one package id. Composed from <see cref="FloorVersion" /> so
    ///     that the version this asserts and the version a failure quotes cannot come apart.
    /// </summary>
    /// <param name="package">The package id the reference names.</param>
    private static string FloorReferenceOpening(string package)
    {
        return "<PackageReference Include=\"" + package + "\" Version=\"" + FloorVersion + "\"";
    }

    /// <summary>The same reference with its assets kept private.</summary>
    /// <param name="package">The package id the reference names.</param>
    private static string PrivateReference(string package)
    {
        return FloorReferenceOpening(package) + " PrivateAssets=\"all\" />";
    }

    /// <summary>
    ///     The prefix every compiler-API package shares, and with it the family wildcard that an ignore
    ///     might use.
    /// </summary>
    private const string CompilerApiFamily = "Microsoft.CodeAnalysis";

    /// <summary>The compiler API itself, which is what the floor is a version of.</summary>
    private const string CompilerPackage = "Microsoft.CodeAnalysis.CSharp";

    /// <summary>
    ///     The editor's completion surface, which ships in the same release as the compiler API and brings
    ///     the Workspaces half of it. A project loaded inside an editor lives by the same floor through this
    ///     package as one loaded inside the compiler does through the other.
    /// </summary>
    private const string FeaturesPackage = "Microsoft.CodeAnalysis.Features";

    /// <summary>
    ///     Every declaration that states the floor, and whether it keeps the package's assets private, which is how a
    ///     project that must not pass the compiler API on spells it: a package flowing on from there would put a copy
    ///     at the floor in front of everything that references the project.
    /// </summary>
    private static readonly (string Csproj, string Package, bool Private)[] FloorDeclarations =
    [
        // The extraction project, where the reference flows on to consumers.
        (ExtractionCsproj, CompilerPackage, false),

        // The spec analyzer states the floor itself, because its one project reference is the contract and there
        // is no compiler API to inherit through it.
        (SpecAnalyzerCsproj, CompilerPackage, true),

        // The completion provider references no project in this repository at all, so both halves of what it
        // loads are its own literals — the compiler API, and the completion surface that brings the Workspaces
        // half of the same release.
        (SpecCompletionCsproj, CompilerPackage, true),
        (SpecCompletionCsproj, FeaturesPackage, true)
    ];

    /// <summary>Every package a project in this repository states the floor through, each one ignored by name.</summary>
    private static readonly string[] FloorPackages = FloorDeclarations
        .Select(static declaration => declaration.Package)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    ///     The one <c>Microsoft.CodeAnalysis</c> package that is not the compiler API: an analyzer that
    ///     runs over a project's own source at build time, private to it and reaching no consumer. Pinning
    ///     it above the floor says nothing about which compiler the product runs inside, so a reference to
    ///     it is not a second statement of the floor.
    /// </summary>
    private const string BuildTimeAnalyzerPackage = "Microsoft.CodeAnalysis.Analyzers";

    /// <summary>
    ///     The compiler-API assemblies an analyzer running inside the compiler binds against: the C#
    ///     half and the language-independent half it pins exactly. Both have to land on the floor, because
    ///     a graph that raised either raised the oldest compiler the analyzer loads in.
    /// </summary>
    private static readonly string[] CompilerApiPackages =
        ["Microsoft.CodeAnalysis.CSharp", "Microsoft.CodeAnalysis.Common"];

    // What a reader of a failure to hold the floor has to weigh before changing anything, whichever half
    // failed: the number is a support range, and the only way it moves silently is an accepted bump.
    private const string Header =
        "The compiler-API floor has moved, or stopped being held. Microsoft.CodeAnalysis.CSharp "
        + FloorVersion + " is the oldest compiler API SlideRule runs against, and every host that loads "
        + "the extraction package brings its own: an analyzer, a source generator, an MSBuild task, a tool "
        + "running inside a compiler. Raising the floor drops support for every one of them pinned below "
        + "the new version, so it is a decision about who the product still runs for — taken deliberately, "
        + "with a release note — and never something to accept from an automated dependency bump, which is "
        + "the only way it moves on its own";

    private const string OverreachHeader =
        "An ignore that holds the compiler-API floor also stops a reference meant to stay current from "
        + "being updated. The ignore applies to every project an update run reaches, so it has to name the "
        + "floor packages exactly: the family wildcard also matches the workspace packages and the build-time "
        + "analyzer, and neither says anything about which compiler the product runs inside";

    private static readonly Regex CompilerApiReference =
        new(@"<PackageReference\s+Include=""Microsoft\.CodeAnalysis", RegexOptions.CultureInvariant);

    [Fact]
    public void EveryFloorProject_StatesTheFloor()
    {
        // Arrange
        List<string> findings = new();

        // Act
        foreach ((string csproj, string package, bool isPrivate) in FloorDeclarations)
        {
            string expected = isPrivate ? PrivateReference(package) : FloorReferenceOpening(package) + " />";
            if (FloorFindingFor(csproj, expected) is { } finding) findings.Add(finding);
        }

        // Assert
        findings.ShouldReportNothing(Header);
    }

    [Fact]
    public void EveryUpdateEntry_IgnoresTheFloorPackages()
    {
        // Act: every entry rather than the one that lists a floor project, since a run bumps packages in
        // whatever its projects reach.
        IReadOnlyList<string> findings = DependabotEntries.MissingIgnores(
            FloorPackages,
            "raises the floor in every project that states it and that its projects reach");

        // Assert
        findings.ShouldReportNothing(Header);
    }

    [Fact]
    public void TheCompilerApiIgnores_MatchOnlyTheFloorDeclarations()
    {
        // Arrange: every compiler-API name any entry ignores, wildcards included, held against every tracked
        // project's references, because an ignore freezes whatever it matches in any project a run reaches.
        List<string> ignored = DependabotEntries.NuGetEntries()
            .SelectMany(DependabotEntries.IgnoredNames)
            .Where(static name => name.StartsWith(CompilerApiFamily, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        IReadOnlyList<(string Project, DeclaredPackage Package)> declared = ProjectPackages.Tracked;
        List<string> findings = new();

        // Act
        foreach (string name in ignored)
        {
            Regex pattern = IgnorePattern(name);
            findings.AddRange(declared
                .Where(site => pattern.IsMatch(site.Package.Id) && site.Package.Version != FloorVersion)
                .Select(site => $"{DependabotConfig} ignores {name}, which also matches {site.Package.Id} "
                                + $"{site.Package.Version} in {site.Project}."));
        }

        // Assert
        findings.ShouldReportNothing(OverreachHeader);
    }

    [Fact]
    public void TheAnalyzerProject_ResolvesTheFloorThroughExtractionRatherThanDeclaringItAgain()
    {
        // Arrange: the analyzer is loaded by the compiler and binds against the compiler API the host
        // brings, so it lives by the same floor. It names no compiler-API package of its own, because a
        // second literal is a second place the floor would have to be raised and a second place it could
        // be raised without the first. What holds it is the resolved graph the lock file records.
        IReadOnlyDictionary<string, LockedPackage> resolved = LockedPackages();
        List<string> findings = new();

        // Act
        findings.AddRange(CompilerApiReferences(DocPaths.AnalyzersProject)
            .Select(line => $"{DocPaths.AnalyzersProject} declares {line}, which is a second place the floor is "
                            + "stated and a second place it can move."));

        foreach (string package in CompilerApiPackages)
        {
            if (!resolved.TryGetValue(package, out LockedPackage? entry))
            {
                findings.Add($"{AnalyzerLockFile} resolves no {package} at all, so the analyzer is not "
                             + "built against the compiler API the floor names.");
                continue;
            }

            if (entry.Type != TransitiveEntry)
                findings.Add($"{AnalyzerLockFile} resolves {package} as a {entry.Type} entry rather than a "
                             + $"{TransitiveEntry} one, so it no longer arrives through "
                             + $"{ExtractionCsproj} and the floor is stated twice.");

            if (entry.Resolved != FloorVersion)
                findings.Add($"{AnalyzerLockFile} resolves {package} at {entry.Resolved}, not "
                             + $"{FloorVersion}.");
        }

        // Assert
        findings.ShouldReportNothing(Header);
    }

    // What a project file that is supposed to state the floor says instead, or nothing when it states it.
    private static string? FloorFindingFor(string csproj, string expected)
    {
        List<string> declared = CompilerApiReferences(csproj);
        bool statesTheFloor = declared.Any(line => string.Equals(line, expected, StringComparison.Ordinal));
        if (statesTheFloor) return null;

        string carried = declared.Count == 0
            ? "no Microsoft.CodeAnalysis reference at all"
            : string.Join(", ", declared);

        return $"{csproj} carries {carried}, not {expected}.";
    }

    // Every compiler-API reference a project file declares, trimmed to the element itself so a comparison
    // against the expected literal does not turn on indentation, and less the build-time analyzer package,
    // which shares the prefix without being part of the support range.
    private static List<string> CompilerApiReferences(string csproj)
    {
        return RepoRoot.ReadLines(csproj)
            .Select(static line => line.Trim())
            .Where(static line => CompilerApiReference.IsMatch(line))
            .Where(static line => !line.Contains($"Include=\"{BuildTimeAnalyzerPackage}\"", StringComparison.Ordinal))
            .ToList();
    }

    // An ignore's dependency name as the match Dependabot makes: `*` stands for any run of characters, the
    // rest is literal, and the whole id has to match. Case is ignored, as NuGet ignores it in an id.
    private static Regex IgnorePattern(string name)
    {
        IEnumerable<string> literals = name.Split('*')
            .Select(Regex.Escape);
        string body = string.Join(".*", literals);

        return new Regex($"^{body}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    // The analyzer's committed lock file, read as the single target-framework group it carries. Only the
    // two facts a floor is read from are kept: how the graph reached an entry, and which version it
    // settled on.
    private static IReadOnlyDictionary<string, LockedPackage> LockedPackages()
    {
        using JsonDocument document = JsonDocument.Parse(RepoRoot.ReadText(AnalyzerLockFile));
        JsonElement dependencies = document.RootElement.GetProperty("dependencies");
        JsonProperty framework = dependencies.EnumerateObject()
            .Single();

        // A project entry carries no resolved version, so a missing key is an empty string rather than a throw.
        return framework.Value.EnumerateObject()
            .ToDictionary(
                static entry => entry.Name,
                static entry => new LockedPackage(
                    entry.Value.StringOrDefault("type") ?? "",
                    entry.Value.StringOrDefault("resolved") ?? ""),
                StringComparer.Ordinal);
    }

    /// <summary>
    ///     One entry of a committed lock file, reduced to what a floor is read from: how the graph reached
    ///     it (<paramref name="Type" />) and the version it settled on (<paramref name="Resolved" />).
    /// </summary>
    private sealed record LockedPackage(string Type, string Resolved);
}
