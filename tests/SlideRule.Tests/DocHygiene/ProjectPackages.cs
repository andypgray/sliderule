using System.Xml.Linq;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>The package references a project file declares, read as XML for the gates that hold them.</summary>
internal static class ProjectPackages
{
    private static readonly Lazy<IReadOnlyList<(string Project, DeclaredPackage Package)>> LazyTracked = new(static () =>
        TrackedFiles.Projects
            .SelectMany(static project => Declared(project)
                .Select(package => (Project: project, Package: package)))
            .ToList());

    /// <summary>
    ///     Every package reference each tracked project file declares, with the project that declares it: the
    ///     projects in tracked order, each one's references in file order. Read once per run.
    /// </summary>
    internal static IReadOnlyList<(string Project, DeclaredPackage Package)> Tracked => LazyTracked.Value;

    /// <summary>
    ///     Every package reference the project file declares with both an <c>Include</c> and a <c>Version</c>
    ///     attribute, in file order.
    /// </summary>
    internal static IReadOnlyList<DeclaredPackage> Declared(string csproj)
    {
        XDocument project = XDocument.Parse(RepoRoot.ReadText(csproj));
        IEnumerable<XElement> references = project.Descendants("PackageReference")
            .Where(static element => element.Attribute("Include") is not null
                                     && element.Attribute("Version") is not null);

        return references
            .Select(static element => new DeclaredPackage(
                (string)element.Attribute("Include")!,
                (string)element.Attribute("Version")!,
                string.Equals((string?)element.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase),
                string.Equals((string?)element.Attribute("ExcludeAssets"), "runtime", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}

/// <summary>
///     One package reference as a project file declares it: the id it includes (<paramref name="Id" />), the
///     version it names (<paramref name="Version" />), whether <c>PrivateAssets</c> is <c>all</c>, so the reference
///     reaches no consumer (<paramref name="IsPrivate" />), and whether <c>ExcludeAssets</c> is <c>runtime</c>
///     (<paramref name="ExcludesRuntime" />).
/// </summary>
internal sealed record DeclaredPackage(string Id, string Version, bool IsPrivate, bool ExcludesRuntime);
