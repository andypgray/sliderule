using System.Xml.Linq;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     One project the solution file declares, with the facts about it the gates over this repository's
///     hand-written project lists are derived from.
/// </summary>
/// <param name="Name">The project file's base name, which is how every list here spells a project.</param>
/// <param name="CsprojPath">
///     The project file's repository-relative path with forward slashes, exactly as the solution spells it.
/// </param>
/// <param name="Folder">The solution folder that holds it, slashes included — <c>/src/</c>, <c>/arch/</c>.</param>
/// <param name="AssemblyName">
///     Its <c>AssemblyName</c>, or <paramref name="Name" /> where the project file declares none, which is
///     what the SDK would default it to.
/// </param>
/// <param name="PackageId">
///     Its <c>PackageId</c>, or <paramref name="AssemblyName" /> where the project file declares none, which
///     is what the SDK's pack would default it to.
/// </param>
/// <param name="TargetFramework">
///     Its declared target framework, or the whole <c>TargetFrameworks</c> list where it declares several,
///     or an empty string where it declares neither.
/// </param>
/// <param name="IsPackable">
///     Whether it packs: <see langword="false" /> only where the project file says <c>IsPackable</c> is
///     false, because the SDK packs by default and silence means yes.
/// </param>
/// <param name="IsTool">Whether it packs as a .NET tool rather than as a library.</param>
/// <param name="IncludeBuildOutput">
///     Whether the package it builds carries that build output: <see langword="false" /> only where the
///     project file says <c>IncludeBuildOutput</c> is false, because the SDK packs the assembly into
///     <c>lib/</c> by default and silence means yes. A package that says no puts its assemblies somewhere
///     the consumer never compiles against, which is what an analyzer does.
/// </param>
internal sealed record SolutionProject(
    string Name,
    string CsprojPath,
    string Folder,
    string AssemblyName,
    string PackageId,
    string TargetFramework,
    bool IsPackable,
    bool IsTool,
    bool IncludeBuildOutput);

/// <summary>
///     What the solution file declares, project by project: the one source the gates over this
///     repository's hand-written project lists and prose counts read their expected answer from.
/// </summary>
/// <remarks>
///     Committed bytes only: the solution and the project files are parsed as XML, and nothing here loads a
///     workspace, runs MSBuild or evaluates a property, so every gate built on it stays cheap,
///     cross-platform and parallel-safe. The cost is that an imported or conditioned property is invisible,
///     and what is read is what a project file declares for itself at the top level — which is also the
///     granularity the lists being held are written at.
/// </remarks>
internal static class SolutionInventory
{
    /// <summary>The solution folder holding what this repository ships.</summary>
    private const string SrcFolder = "/src/";

    private static readonly Lazy<IReadOnlyList<SolutionProject>> LazyAll = new(Read);

    private static readonly Lazy<IReadOnlyList<SolutionProject>> LazySrc =
        new(() => InFolders(SrcFolder));

    private static readonly Lazy<IReadOnlyList<SolutionProject>> LazyDrawn =
        new(() => InFolders(SrcFolder, "/arch/")
            .OrderBy(DrawnRank)
            .ThenBy(static project => project.Name, StringComparer.Ordinal)
            .ToArray());

    private static readonly Lazy<IReadOnlyList<SolutionProject>> LazyPackable =
        new(() => LazySrc.Value
            .Where(static project => project.IsPackable)
            .ToArray());

    /// <summary>Every project the solution declares, in declaration order.</summary>
    public static IReadOnlyList<SolutionProject> All => LazyAll.Value;

    /// <summary>What <c>src/</c> declares, ordered by name.</summary>
    public static IReadOnlyList<SolutionProject> SrcProjects => LazySrc.Value;

    /// <summary>
    ///     The projects the committed diagram draws: everything the solution declares outside the tests
    ///     and their fixtures.
    /// </summary>
    /// <remarks>
    ///     Ordered the way <c>sliderule.json</c>'s <c>render.diagram.only</c> list is written rather than by
    ///     name — what ships first, then the rest of <c>src/</c>, then the spec project — because a plain
    ///     sort by name puts the spec project second and would move the committed drawing. The order is
    ///     derived from what each project is, so a new one takes its place in it without anyone choosing.
    /// </remarks>
    public static IReadOnlyList<SolutionProject> DrawnProjects => LazyDrawn.Value;

    /// <summary>The <c>src/</c> projects that pack — the packages a release publishes.</summary>
    public static IReadOnlyList<SolutionProject> PackableProjects => LazyPackable.Value;

    /// <summary>
    ///     The MSBuild properties a project or props file declares for itself: the children of its
    ///     top-level <c>PropertyGroup</c>s, keyed ordinally by element name, the last declaration of a
    ///     name winning as MSBuild's own evaluation would have it.
    /// </summary>
    /// <remarks>
    ///     No condition is evaluated. A conditioned group, and a conditioned property inside a group, are
    ///     read as though they were unconditioned, and a group nested inside a target or a choose is not
    ///     read at all. What the callers ask is what a file says about itself — the text a reader of that
    ///     file sees, and the text an outside scanner that never evaluates MSBuild sees too.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Properties(string repoRelativePath)
    {
        XDocument document = XDocument.Parse(RepoRoot.ReadText(repoRelativePath));
        IEnumerable<XElement> groups = document.Root!.Elements("PropertyGroup");

        return groups.SelectMany(static group => group.Elements())
            .GroupBy(static property => property.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last()
                    .Value.Trim(),
                StringComparer.Ordinal);
    }

    // What ships, then the rest of src/, then everything else — the reading order the diagram arguments
    // are written in. Three ranks rather than a folder comparison because the distinction the order makes
    // is what a project is for, and the rule pack sits in src/ beside the packages without shipping.
    private static int DrawnRank(SolutionProject project)
    {
        if (project.IsPackable) return 0;

        return project.Folder == SrcFolder ? 1 : 2;
    }

    private static IReadOnlyList<SolutionProject> InFolders(params string[] folders)
    {
        return LazyAll.Value
            .Where(project => folders.Contains(project.Folder, StringComparer.Ordinal))
            .OrderBy(static project => project.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<SolutionProject> Read()
    {
        XDocument solution = XDocument.Parse(File.ReadAllText(RepoRoot.Solution));

        return solution.Descendants("Project")
            .Select(Declared)
            .ToArray();
    }

    private static SolutionProject Declared(XElement declaration)
    {
        string path = declaration.Attribute("Path")!
            .Value.Replace('\\', '/');
        string name = Path.GetFileNameWithoutExtension(path);
        IReadOnlyDictionary<string, string> properties = Properties(path);
        string assemblyName = Declares(properties, "AssemblyName") ?? name;

        return new SolutionProject(
            name,
            path,
            FolderOf(declaration),
            assemblyName,
            Declares(properties, "PackageId") ?? assemblyName,
            Declares(properties, "TargetFramework") ?? Declares(properties, "TargetFrameworks") ?? string.Empty,
            !IsSet(properties, "IsPackable", "false"),
            IsSet(properties, "PackAsTool", "true"),
            !IsSet(properties, "IncludeBuildOutput", "false"));
    }

    // The nearest enclosing solution folder, or "/" for a project the solution declares at its root.
    private static string FolderOf(XElement declaration)
    {
        XElement? folder = declaration.Ancestors("Folder")
            .FirstOrDefault();

        return folder?.Attribute("Name")
            ?.Value ?? "/";
    }

    private static string? Declares(IReadOnlyDictionary<string, string> properties, string property)
    {
        return properties.TryGetValue(property, out string? declared) ? declared : null;
    }

    private static bool IsSet(IReadOnlyDictionary<string, string> properties, string property, string value)
    {
        return string.Equals(Declares(properties, property), value, StringComparison.OrdinalIgnoreCase);
    }
}
