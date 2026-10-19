using System.Text.RegularExpressions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over the MSBuild line: the four <c>Microsoft.Build</c> engine packages and
///     <c>Microsoft.NET.StringTools</c> are pinned on the minor line that ships assets for the target framework of
///     every project that declares them, and held there against automated dependency updates by a version-bounded
///     ignore on every nuget entry of the Dependabot configuration.
/// </summary>
/// <remarks>
///     <para>
///         Each MSBuild minor line ships for its own .NET only. The 18.9 line carries net10.0; the 18.10 line
///         carries net11.0 and net472 and nothing for net10.0, so on a net10.0 project <c>Microsoft.Build</c> 18.10
///         resolves its .NET Framework assemblies through the fallback, with NU1701 and the Framework polyfills it
///         brings joining the graph. Dependabot proposes whatever is newest, so it proposes that. The src build
///         promotes NU1701 to an error, so a pin that leaves the line already fails restore; this class holds what
///         the build cannot see.
///     </para>
///     <para>
///         Three things are held to each other. The pins share one version on the held line, so the five move in
///         lockstep and sit directly below the bound every ignore names; the ignores are what stop a weekly update
///         run from moving them to the next line, while still letting patches on this line through; and the target
///         framework is what the line was chosen for, so a move to net11.0 reds here as the reminder to lift the
///         ignores and take the next line with it.
///     </para>
///     <para>
///         The ignore is held on every nuget entry, for the reason the compiler-API floor's is: an update run bumps
///         packages in every project its listed projects reach through a <c>ProjectReference</c>.
///     </para>
/// </remarks>
public sealed class MsBuildLinePinTests
{
    private const string DependabotConfig = DependabotEntries.ConfigPath;

    /// <summary>The target framework the held line ships assets for.</summary>
    private const string TargetFramework = "net10.0";

    /// <summary>The MSBuild minor line that ships assets for <see cref="TargetFramework" />.</summary>
    private const string HeldLine = "18.9";

    /// <summary>The first line past the held one, which every ignore names as its lower bound.</summary>
    private const string NextLine = "18.10";

    /// <summary>The <c>versions:</c> value each ignore carries, composed from <see cref="NextLine" />.</summary>
    private const string IgnoredVersions = "[\">= " + NextLine + "\"]";

    /// <summary>The engine packages and the string-interning utility they depend on, which move in lockstep.</summary>
    private static readonly string[] LinePackages =
    [
        "Microsoft.Build",
        "Microsoft.Build.Framework",
        "Microsoft.Build.Tasks.Core",
        "Microsoft.Build.Utilities.Core",
        "Microsoft.NET.StringTools"
    ];

    // What a reader of any failure here has to weigh before changing anything.
    private const string Header =
        "The MSBuild packages have left the line their target framework is served by, or stopped being held there. "
        + "Each MSBuild minor line ships assets for its own .NET only: " + HeldLine + " carries " + TargetFramework
        + ", while " + NextLine + " carries net11.0 and net472, so on a " + TargetFramework + " project "
        + "Microsoft.Build " + NextLine + " compiles against its .NET Framework assemblies. Move the line only "
        + "together with the target framework, and the ignores with both";

    private static readonly Regex TargetFrameworkElement =
        new(@"<TargetFrameworks?>(?<value>[^<]+)</TargetFrameworks?>", RegexOptions.CultureInvariant);

    [Fact]
    public void EveryDeclaredLinePackage_SharesOneVersionOnTheHeldLine()
    {
        // Arrange
        List<(string Project, DeclaredPackage Package)> declared = LineDeclarations();
        List<string> findings = new();

        // Act: no declaration at all would make the line held over nothing.
        if (declared.Count == 0)
            findings.Add("No tracked project declares any of " + string.Join(", ", LinePackages) + ".");

        findings.AddRange(declared
            .Where(static site => !site.Package.Version.StartsWith(HeldLine + ".", StringComparison.Ordinal))
            .Select(static site => $"{site.Project} pins {site.Package.Id} {site.Package.Version}, "
                                   + $"not a {HeldLine}.x version."));

        List<string> versions = declared.Select(static site => site.Package.Version)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (versions.Count > 1)
            findings.Add($"The line packages are pinned at {string.Join(", ", versions)}, not one version: "
                         + string.Join("; ", declared.Select(static site => $"{site.Project} {site.Package.Id} {site.Package.Version}")));

        // Assert
        findings.ShouldReportNothing(Header);
    }

    [Fact]
    public void EveryUpdateEntry_IgnoresTheNextLineOfEveryLinePackage()
    {
        // Arrange
        IReadOnlyList<string[]> entries = DependabotEntries.NuGetEntries();
        List<string> findings = new();

        // Act
        if (entries.Count == 0)
            findings.Add($"{DependabotConfig} carries no nuget update entry, so nothing holds the MSBuild line.");

        foreach (string[] entry in entries)
        {
            // Every ignore of a package is compared, not the first: Dependabot applies them all, so a second
            // ignore with other versions moves what the package is held to.
            ILookup<string, string?> ignored = DependabotEntries.Ignores(entry)
                .ToLookup(static ignore => ignore.Name, static ignore => ignore.Versions, StringComparer.Ordinal);
            string directories = string.Join(", ", DependabotEntries.Directories(entry));
            foreach (string package in LinePackages)
            {
                if (!ignored.Contains(package))
                    findings.Add($"{DependabotConfig}'s nuget entry for {directories} carries no 'ignore:' naming "
                                 + $"{package}, so its next update run moves it to the {NextLine} line.");

                findings.AddRange(ignored[package]
                    .Where(static versions => versions != IgnoredVersions)
                    .Select(versions => $"{DependabotConfig}'s nuget entry for {directories} ignores {package} with "
                                        + $"versions {versions ?? "(none, so every version)"}, not {IgnoredVersions}."));
            }
        }

        // Assert
        findings.ShouldReportNothing(Header);
    }

    [Fact]
    public void EveryProjectDeclaringALinePackage_TargetsTheFrameworkTheLineServes()
    {
        // Arrange
        List<string> projects = LineDeclarations()
            .Select(static site => site.Project)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        List<string> findings = new();

        // Act
        if (projects.Count == 0)
            findings.Add("No tracked project declares any of " + string.Join(", ", LinePackages) + ".");

        foreach (string project in projects)
        {
            List<string> frameworks = TargetFrameworkElement.Matches(RepoRoot.ReadText(project))
                .Select(static match => match.Groups["value"].Value.Trim())
                .ToList();
            if (frameworks.Count != 1 || frameworks[0] != TargetFramework)
                findings.Add($"{project} targets {(frameworks.Count == 0 ? "nothing it states" : string.Join(", ", frameworks))}, "
                             + $"not {TargetFramework} alone.");
        }

        // Assert
        findings.ShouldReportNothing(Header);
    }

    // Every declaration of a line package across the tracked projects, so a new project that takes one is
    // held without being registered here.
    private static List<(string Project, DeclaredPackage Package)> LineDeclarations()
    {
        return ProjectPackages.Tracked
            .Where(static site => LinePackages.Contains(site.Package.Id, StringComparer.Ordinal))
            .ToList();
    }
}
