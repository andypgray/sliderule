using System.Text.RegularExpressions;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The xUnit adapter carries the Roslyn project inside its own package, so it declares for itself what that
///     project would have passed on to a consumer. Two lists therefore name the same packages, and this gate holds
///     them together. It also holds the adapter's shipped build file to removing from a consumer's test output the
///     runtime assets of exactly the MSBuild engine packages the adapter keeps out of its own runtime.
/// </summary>
/// <remarks>
///     A version bumped on one side only would restore and build here, where the adapter reaches the Roslyn project
///     by project reference, and diverge only in a consumer's graph, which nothing in the suite builds.
/// </remarks>
public sealed class AdapterCarriedPackagesTests
{
    private const string RoslynProject = "src/SlideRule.Roslyn/SlideRule.Roslyn.csproj";

    private const string AdapterBuildFile = "src/SlideRule.Xunit/buildTransitive/SlideRule.Xunit.targets";

    private static readonly Regex RemovedPackage = new(
        @"%\(RuntimeCopyLocalItems\.NuGetPackageId\)' == '(?<id>[^']+)'", RegexOptions.CultureInvariant);

    [Fact]
    public void EveryPackageTheRoslynProjectPassesOn_IsDeclaredByTheAdapterAtTheSameVersion()
    {
        // Arrange
        IReadOnlyList<DeclaredPackage> passedOn = ProjectPackages.Declared(RoslynProject)
            .Where(static line => !line.IsPrivate)
            .ToList();
        Dictionary<string, DeclaredPackage> adapter = ProjectPackages.Declared(DocPaths.AdapterProject)
            .ToDictionary(static line => line.Id, StringComparer.Ordinal);

        // Act
        List<string> findings = passedOn
            .Where(line => adapter.GetValueOrDefault(line.Id)?.Version != line.Version)
            .Select(line => $"{line.Id} {line.Version} is passed on by the Roslyn project, and the adapter declares "
                            + (adapter.GetValueOrDefault(line.Id)?.Version ?? "nothing"))
            .ToList();

        // Assert
        passedOn.ShouldNotBeEmpty("the Roslyn project passes on no package, so this gate would hold over nothing");
        findings.ShouldReportNothing(
            "The adapter carries the Roslyn project, so it declares every package that project passes on, at the "
            + "same version; bump both lines together");
    }

    [Fact]
    public void EveryEnginePackageTheAdapterKeepsOutOfItsRuntime_IsPinnedAlikeAndRemovedFromAConsumersOutput()
    {
        // Arrange
        Dictionary<string, DeclaredPackage> roslyn = ProjectPackages.Declared(RoslynProject)
            .ToDictionary(static line => line.Id, StringComparer.Ordinal);
        List<DeclaredPackage> engine = ProjectPackages.Declared(DocPaths.AdapterProject)
            .Where(static line => line.ExcludesRuntime)
            .ToList();
        HashSet<string> removed = RemovedPackage.Matches(RepoRoot.ReadText(AdapterBuildFile))
            .Select(static match => match.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Act
        List<string> findings = [];
        foreach (DeclaredPackage line in engine)
        {
            if (!roslyn.TryGetValue(line.Id, out DeclaredPackage? pinned) || pinned.Version != line.Version)
                findings.Add($"{line.Id} {line.Version} is kept out of the adapter's runtime, but the Roslyn project pins "
                             + (pinned?.Version ?? "nothing"));
            if (!removed.Contains(line.Id))
                findings.Add($"{line.Id} is kept out of the adapter's runtime, but its build file does not remove it "
                             + "from a consumer's test output");
        }

        findings.AddRange(removed
            .Where(id => engine.All(line => line.Id != id))
            .Select(static id => $"the build file removes {id}, which the adapter does not keep out of its own runtime"));

        // Assert
        engine.ShouldNotBeEmpty("the adapter keeps no engine package out of its runtime, so this gate would hold over nothing");
        findings.ShouldReportNothing(
            "The adapter keeps MSBuild's engine packages out of its runtime and out of every consumer's, so the "
            + "versions follow the Roslyn project and the build file names exactly the same packages");
    }
}
