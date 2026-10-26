using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using SlideRule.Internal;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     Which projects declare a <c>&lt;Reference&gt;</c> the build left unresolved, answered by comparing the
///     literal items in each <c>.csproj</c> with the metadata references its compilation received. The build drops
///     such a reference before the compiler command line, so neither load path reports it and types from that
///     assembly bind to nothing.
/// </summary>
/// <remarks>
///     <para>
///         The project file is read because Roslyn's build host never exposes ResolveAssemblyReferences' items, so
///         the declared side has no other source. Only a literal item outside any <c>Condition</c> counts, and a
///         name counts as resolved when any metadata reference's file carries it, whatever HintPath it was
///         declared with: a stale HintPath the build satisfied from the framework or the GAC is not reported.
///     </para>
///     <para>
///         Every read fault answers "nothing unresolved", the posture <see cref="RestoreFailures" /> takes: the
///         finding is a warning, and a file this cannot read is no evidence of a missing assembly.
///     </para>
/// </remarks>
internal static class UnresolvedReferences
{
    /// <summary>
    ///     Each C# project, by its absolute <c>.csproj</c> path, whose declared references no leg of its build
    ///     resolved, ordinal-sorted by path, with those references' simple names ordinal-sorted. The projects in
    ///     <paramref name="alreadyFailed" /> are left out.
    /// </summary>
    internal static IReadOnlyList<Finding> Detect(Solution solution, IReadOnlyList<string> alreadyFailed)
    {
        // A multi-target-framework project is several Projects behind one csproj, so the resolved names are
        // gathered across its legs before the declared ones are compared with them.
        var resolvedByProjectPath = new Dictionary<string, HashSet<string>>(PathComparison.Comparer);

        foreach (Project project in solution.Projects)
        {
            if (project.FilePath is not { } filePath || !ProjectLanguages.IsCSharp(project)) continue;

            string fullPath = Path.GetFullPath(filePath);
            if (!resolvedByProjectPath.TryGetValue(fullPath, out HashSet<string>? resolved))
            {
                resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                resolvedByProjectPath[fullPath] = resolved;
            }

            IEnumerable<string> names = project.MetadataReferences
                .OfType<PortableExecutableReference>()
                .Select(reference => Path.GetFileNameWithoutExtension(reference.FilePath))
                .OfType<string>();
            resolved.UnionWith(names);
        }

        var failed = new HashSet<string>(alreadyFailed, PathComparison.Comparer);

        return resolvedByProjectPath
            .Where(entry => !failed.Contains(entry.Key))
            .Select(entry => new Finding(entry.Key, Unresolved(entry.Key, entry.Value)))
            .Where(finding => finding.Names.Count > 0)
            .OrderBy(finding => finding.ProjectPath, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     The load warning for <paramref name="projectPath" />, which declares the references
    ///     <paramref name="names" /> and received none of them.
    /// </summary>
    internal static string Message(string projectPath, IReadOnlyList<string> names)
    {
        string references = Plurals.Counted(names.Count, "reference");
        string assemblies = names.Count == 1 ? "that assembly" : "those assemblies";
        string pronoun = names.Count == 1 ? "it" : "them";
        return $"'{projectPath}' declares {references} the build could not resolve ({string.Join(", ", names)}); "
               + $"types from {assemblies} are unbound, so edges to {pronoun} are absent from the model.";
    }

    private static List<string> Unresolved(string projectPath, HashSet<string> resolved)
    {
        if (ProjectFileXml.TryReadProjectRoot(projectPath) is not { } root) return [];

        return root.Elements()
            .Where(element => element.Name.LocalName == "ItemGroup" && !HasCondition(element))
            .SelectMany(group => group.Elements())
            .Where(element => element.Name.LocalName == "Reference" && !HasCondition(element))
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .SelectMany(include => include.Split(';'))
            .Select(SimpleName)
            .OfType<string>()
            .Where(name => !resolved.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    // Null for an empty entry and for one MSBuild would expand: a property, an item list or transform, a wildcard.
    // An identity's text up to its first comma is the simple name, and a path-form Include names the file.
    private static string? SimpleName(string include)
    {
        string item = include.Trim();
        if (item.Length == 0 || item.Contains("$(") || item.Contains("@(") || item.Contains("%(")
            || item.IndexOfAny(['*', '?']) >= 0)
            return null;

        int comma = item.IndexOf(',');
        string name = comma < 0 ? item : item[..comma].TrimEnd();

        int separator = name.LastIndexOfAny(['/', '\\']);
        if (separator >= 0) name = name[(separator + 1)..];

        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return name.Length == 0 ? null : name;
    }

    private static bool HasCondition(XElement element)
    {
        return element.Attributes()
            .Any(attribute => attribute.Name.LocalName == "Condition");
    }

    /// <summary>A project and the simple names of the references it declares that its build left unresolved.</summary>
    internal sealed record Finding(string ProjectPath, IReadOnlyList<string> Names);
}
