using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SlideRule.Internal;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     A solution file's declared membership: the <c>.csproj</c> half split into the two sets a solution filter
///     makes different, what the load is entitled to demand (<paramref name="Required" />) and what the
///     underlying solution builds (<paramref name="Declared" />), beside the
///     <paramref name="Unsupported" /> entries no load can reach and the <paramref name="NotBuilt" /> projects the
///     solution's default configuration leaves out of the build. For an unfiltered <c>.sln</c>/<c>.slnx</c> the
///     first two are the same list.
/// </summary>
/// <remarks>
///     <paramref name="Required" /> is the built members a filter selects, or all of them when there is no filter or
///     the filter's <c>projects</c> array is empty, which Roslyn reads as "the whole solution"; a member of it
///     that did not load is a load failure. Subtracting what loaded from <paramref name="Declared" /> is what
///     makes a narrowed universe nameable. <paramref name="Unsupported" /> stays out of
///     <paramref name="Declared" />, whose consumers read it as "a project this run was obliged to check":
///     adding a project no extractor can read would turn every polyglot solution into a load failure.
/// </remarks>
internal sealed record SolutionMembership(
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Declared,
    IReadOnlyList<UnsupportedProject> Unsupported,
    IReadOnlyList<string> NotBuilt);

/// <summary>
///     One solution file's declared projects, absolute, deduplicated and in declaration order, partitioned by
///     whether this product reads them: <paramref name="Csproj" /> is what the extractor loads,
///     <paramref name="Unsupported" /> is every other declared <c>*proj</c> and every Web Site entry, and
///     <paramref name="NotBuilt" /> is the <c>.csproj</c> entries the solution's default configuration does not
///     build. Solution folders are in none of them.
/// </summary>
internal sealed record DeclaredProjects(
    IReadOnlyList<string> Csproj,
    IReadOnlyList<UnsupportedProject> Unsupported,
    IReadOnlyList<string> NotBuilt);

/// <summary>
///     Reads a solution file's declared project membership textually, with no MSBuild, from the classic
///     <c>.sln</c>, the XML <c>.slnx</c>, and the JSON solution-filter <c>.slnf</c> formats; both slash spellings
///     resolve.
/// </summary>
/// <remarks>
///     <para>
///         A filter is a seed set, not the universe. Roslyn loads a filter's projects plus their transitive
///         <c>ProjectReference</c> closure (measured), and <c>DisableTransitiveProjectReferences</c> does not
///         suppress it: that property governs what MSBuild hands the compiler, not what the workspace loader
///         walks. So <see cref="SolutionMembership.Required" /> is what a load must produce, never all it will,
///         and the narrowing is computed from the loaded solution.
///     </para>
///     <para>
///         <see cref="SolutionMembership.Unsupported" /> is read off the solution file rather than the loaded
///         solution, which no non-C# kind but F# ever reaches, so a non-C# project pulled in through a
///         <c>ProjectReference</c> and declared in no solution file is not in it. The load lists such a project
///         from the workspace's failure to open it
///         (<see cref="UnsupportedEntryMessages.OtherLanguageProjectFiles" />).
///     </para>
///     <para>
///         This is a membership oracle, not a solution loader. Of the configurations it reads only whether the
///         solution's default one builds each project, and it evaluates no conditions or nested-project
///         ownership. Paths are made absolute against the
///         solution file's directory but not symlink-canonicalized: callers canonicalize both sides at comparison
///         time, so the <see cref="ParseDeclaredProjects" /> core stays pure and disk-independent.
///     </para>
/// </remarks>
internal static partial class SolutionProjectFileParser
{
    /// <summary>
    ///     Whether this parser owns <paramref name="solutionPath" />'s format — the two full-solution formats
    ///     and the solution filter that points at one. A caller that subtracts declared membership must ask
    ///     this first rather than mistake an unparsed file for "the solution declares nothing".
    /// </summary>
    internal static bool OwnsFormat(string solutionPath)
    {
        string extension = Path.GetExtension(solutionPath);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
               || IsFilterFormat(solutionPath);
    }

    /// <summary>Whether <paramref name="solutionPath" /> is a solution filter (<c>.slnf</c>).</summary>
    internal static bool IsFilterFormat(string solutionPath)
    {
        return Path.GetExtension(solutionPath).Equals(".slnf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Reads <paramref name="solutionPath" />'s membership, resolving a solution filter through to the
    ///     solution it references. Throws on an unreadable or malformed file; callers that must not fail open
    ///     catch it.
    /// </summary>
    /// <remarks>
    ///     Under a <c>.slnf</c> the unsupported and not-built entries are the referenced solution's, unfiltered:
    ///     leaving an <c>.fsproj</c> out of a selection does not make it surveyable, and a not-built project that a
    ///     selected one references must still be left out of the load.
    /// </remarks>
    internal static SolutionMembership ReadDeclaredMembership(string solutionPath)
    {
        string fullPath = Path.GetFullPath(solutionPath);
        string solutionFile = fullPath;
        IReadOnlyList<string> requested = [];
        if (IsFilterFormat(fullPath))
            (solutionFile, requested) = ParseFilter(File.ReadAllText(fullPath), Path.GetDirectoryName(fullPath)!);

        DeclaredProjects projects = ReadDeclaredProjects(solutionFile);
        IReadOnlyList<string> required = projects.Csproj;

        // Roslyn's own rule, reproduced exactly: an empty projects array is not an empty selection, it is
        // "no filtering at all". Intersecting instead would load nothing and report the whole solution
        // dropped — the degraded answer this type exists to avoid, arrived at from the other side.
        if (requested.Count > 0)
        {
            var selected = new HashSet<string>(requested, PathComparison.Comparer);
            required = projects.Csproj
                .Where(selected.Contains)
                .ToList();
        }

        return new SolutionMembership(required, projects.Csproj, projects.Unsupported, projects.NotBuilt);
    }

    /// <summary>
    ///     The pure filter-to-paths core, testable without disk: reads a <c>.slnf</c>'s referenced solution
    ///     and its requested project paths. The two resolve against <em>different</em> bases — the solution
    ///     against <paramref name="filterDirectory" />, each project against the referenced solution's own
    ///     directory — which is the shape Visual Studio writes and Roslyn reads.
    /// </summary>
    /// <exception cref="FormatException">
    ///     The filter is not JSON, declares no solution, or points at another filter (which Roslyn does not
    ///     support).
    /// </exception>
    internal static (string ReferencedSolutionPath, IReadOnlyList<string> RequestedProjects) ParseFilter(
        string filterText, string filterDirectory)
    {
        JsonElement solution;
        try
        {
            using JsonDocument document = JsonDocument.Parse(filterText);
            if (!document.RootElement.TryGetProperty("solution", out JsonElement element))
                throw new FormatException("The solution filter declares no 'solution' object.");

            solution = element.Clone();
        }
        catch (JsonException ex)
        {
            throw new FormatException("The solution filter is not well-formed JSON.", ex);
        }

        if (!solution.TryGetProperty("path", out JsonElement pathElement)
            || pathElement.GetString() is not { Length: > 0 } declaredPath)
            throw new FormatException("The solution filter declares no solution path.");

        string referencedSolution = Path.GetFullPath(Path.Combine(filterDirectory, Normalize(declaredPath)));
        if (IsFilterFormat(referencedSolution))
            throw new FormatException("A solution filter may not reference another solution filter.");

        string solutionDirectory = Path.GetDirectoryName(referencedSolution)!;
        var requested = new List<string>();
        if (solution.TryGetProperty("projects", out JsonElement projects)
            && projects.ValueKind == JsonValueKind.Array)
            requested.AddRange(projects
                .EnumerateArray()
                .Select(entry => entry.GetString())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(Path.Combine(solutionDirectory, Normalize(path!)))));

        return (referencedSolution, requested);
    }

    /// <summary>
    ///     The <c>.sln</c>/<c>.slnx</c> that <paramref name="solutionPath" /> points at when it is a filter,
    ///     or <see langword="null" /> for any other format and for a filter too malformed to resolve. The
    ///     format test comes first, so a plain solution costs no I/O at all.
    /// </summary>
    /// <remarks>
    ///     Best-effort, because it is asked before the workspace load, where a throw would pre-empt the load's own
    ///     filter refusal, the one that names the file and says what a filter must be, with a worse-placed one.
    /// </remarks>
    internal static string? TryReadReferencedSolution(string solutionPath)
    {
        if (!IsFilterFormat(solutionPath)) return null;

        try
        {
            string fullPath = Path.GetFullPath(solutionPath);
            (string referencedSolution, _) =
                ParseFilter(File.ReadAllText(fullPath), Path.GetDirectoryName(fullPath)!);

            return referencedSolution;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    ///     The full path of the solution file a load of <paramref name="solutionPath" /> opens: the
    ///     <c>.sln</c>/<c>.slnx</c> a filter points at, the file itself for any other format, or
    ///     <see langword="null" /> for a filter too malformed to resolve. Whether that file exists is the
    ///     caller's question.
    /// </summary>
    internal static string? SolutionFileOf(string solutionPath)
    {
        string fullPath = Path.GetFullPath(solutionPath);
        return IsFilterFormat(fullPath) ? TryReadReferencedSolution(fullPath) : fullPath;
    }

    /// <summary>
    ///     The directory every convention-relative path of a run over <paramref name="solutionPath" /> anchors
    ///     at: the referenced solution's directory under a <c>.slnf</c>, and the file's own directory
    ///     otherwise. Never throws — a filter that cannot be resolved, or names a solution that is not there,
    ///     anchors at its own directory and leaves the refusal to the load.
    /// </summary>
    /// <remarks>
    ///     A filter is a lens on a solution, not a codebase of its own: baselines, render targets, diff
    ///     resolution and every relativized evidence path belong to the solution it references, or a filter
    ///     kept in a directory of its own would resolve none of them and write its output beside itself.
    /// </remarks>
    internal static string AnchorDirectory(string solutionPath)
    {
        if (TryReadReferencedSolution(solutionPath) is { } referencedSolution
            && File.Exists(referencedSolution))
            return Path.GetDirectoryName(referencedSolution)!;

        return Path.GetDirectoryName(solutionPath)!;
    }

    /// <summary>
    ///     Reads <paramref name="solutionPath" /> from disk and returns the absolute paths of the <c>.csproj</c>
    ///     members its default configuration builds, reading a <c>.slnx</c> as XML and anything else but a solution
    ///     filter as a classic <c>.sln</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     <paramref name="solutionPath" /> is a <c>.slnf</c> solution filter.
    /// </exception>
    internal static IReadOnlyList<string> ReadCsprojMembers(string solutionPath)
    {
        if (IsFilterFormat(solutionPath))
            throw new ArgumentException(
                $"'{solutionPath}' is a solution filter; read the members of the solution it references.",
                nameof(solutionPath));

        return ReadDeclaredProjects(solutionPath).Csproj;
    }

    private static DeclaredProjects ReadDeclaredProjects(string solutionPath)
    {
        string fullPath = Path.GetFullPath(solutionPath);
        string text = File.ReadAllText(fullPath);
        string directory = Path.GetDirectoryName(fullPath)!;
        return ParseDeclaredProjects(text, Path.GetExtension(fullPath), directory);
    }

    /// <summary>
    ///     The pure text-to-paths core, testable without disk: parses <paramref name="solutionText" /> per
    ///     <paramref name="extension" /> (<c>.slnx</c> ⇒ XML, else classic <c>.sln</c>), splits the declared
    ///     entries into the <c>.csproj</c> the default configuration builds, the ones it does not, and every other
    ///     kind of entry, and resolves each against <paramref name="solutionDirectory" /> (normalizing both slash
    ///     spellings). Duplicate paths collapse.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A declared entry is a project when its extension ends in <c>proj</c>. That test takes the whole
    ///         non-C# family (<c>.fsproj</c>, <c>.vbproj</c>, <c>.sqlproj</c>, <c>.vcxproj</c>, <c>.shproj</c>, and
    ///         whatever the next tool mints) with no list to keep current, and excludes solution folders: a classic
    ///         <c>.sln</c> folder line carries a bare name in the path field, and a <c>.slnx</c>
    ///         <c>&lt;Folder&gt;</c> is not a <c>Project</c> element at all. A classic Web Site entry is recognized
    ///         by its project type instead, because its path is a folder or a URL.
    ///     </para>
    ///     <para>
    ///         A <c>.csproj</c> counts as not built only on the evidence MSBuild skips it on: in a <c>.sln</c>, an
    ///         <c>ActiveCfg</c> line for the default configuration with no <c>Build.0</c> line beside it; in a
    ///         <c>.slnx</c>, a <c>Build</c> rule matching the default configuration with <c>Project="false"</c>. A
    ///         project the configuration sections say nothing about is built.
    ///     </para>
    /// </remarks>
    internal static DeclaredProjects ParseDeclaredProjects(
        string solutionText, string extension, string solutionDirectory)
    {
        IEnumerable<SolutionEntry> entries = extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnx(solutionText)
            : ParseSln(solutionText);

        var csproj = new List<string>();
        var unsupported = new List<UnsupportedProject>();
        var notBuilt = new List<string>();
        var seen = new HashSet<string>(PathComparison.Comparer);
        foreach (SolutionEntry entry in entries)
        {
            if (!entry.IsWebSite && !IsProjectEntry(entry.Path)) continue;

            string fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, Normalize(entry.Path)));
            if (!seen.Add(fullPath)) continue;

            if (entry.IsWebSite)
                unsupported.Add(new UnsupportedProject(fullPath, UnsupportedProjectKind.WebSite, entry.Name));
            else if (!entry.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                unsupported.Add(new UnsupportedProject(fullPath, KindOf(entry.Path)));
            else if (entry.NotBuilt) notBuilt.Add(fullPath);
            else csproj.Add(fullPath);
        }

        return new DeclaredProjects(csproj, unsupported, notBuilt);
    }

    // Every entry the "ends in proj" test admits, other than a .csproj and a shared project, is a project this product
    // has no extractor for: another language's, or one that compiles no code, such as a Docker Compose .dcproj. The
    // default arm therefore covers the family this parser cannot enumerate, and only the shared project, which
    // means something else, is named.
    internal static UnsupportedProjectKind KindOf(string relativePath)
    {
        return Path.GetExtension(relativePath)
            .Equals(".shproj", StringComparison.OrdinalIgnoreCase)
            ? UnsupportedProjectKind.SharedProject
            : UnsupportedProjectKind.NotCsharp;
    }

    internal static bool IsProjectEntry(string relativePath)
    {
        return Path.GetExtension(relativePath)
            .EndsWith("proj", StringComparison.OrdinalIgnoreCase);
    }

    // Solution files and filters are both written with whichever slash the authoring tool prefers.
    private static string Normalize(string path)
    {
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static IEnumerable<SolutionEntry> ParseSln(string solutionText)
    {
        HashSet<string> configurationKeys = SectionKeys(solutionText, "ProjectConfigurationPlatforms")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<(string BuildType, string Platform)> configurations = SectionKeys(
                solutionText, "SolutionConfigurationPlatforms")
            .Select(SplitConfiguration)
            .OfType<(string BuildType, string Platform)>()
            .ToList();

        // A solution that declares no configuration gives no evidence that the build skips anything.
        string? configuration = null;
        if (configurations.Count > 0)
        {
            List<string> buildTypes = configurations
                .Select(pair => pair.BuildType)
                .ToList();
            List<string> platforms = configurations
                .Select(pair => pair.Platform)
                .ToList();
            (string buildType, string platform) = DefaultConfiguration(buildTypes, platforms);
            configuration = $"{buildType}|{platform}";
        }

        foreach (Match match in SlnProjectLine().Matches(solutionText))
        {
            string projectGuid = match.Groups["guid"].Value;
            bool notBuilt = configuration is not null
                            && projectGuid.Length > 0
                            && configurationKeys.Contains($"{{{projectGuid}}}.{configuration}.ActiveCfg")
                            && !configurationKeys.Contains($"{{{projectGuid}}}.{configuration}.Build.0");
            bool isWebSite = match.Groups["type"].Value
                .Equals(WebSiteProjectType, StringComparison.OrdinalIgnoreCase);

            yield return new SolutionEntry(match.Groups["path"].Value, match.Groups["name"].Value, isWebSite, notBuilt);
        }
    }

    private static IEnumerable<SolutionEntry> ParseSlnx(string solutionText)
    {
        XDocument document = XDocument.Parse(solutionText);
        List<XElement> configurationElements = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Configurations")
            .ToList();
        List<string> buildTypes = NamesOf(configurationElements, "BuildType");
        List<string> platforms = NamesOf(configurationElements, "Platform");

        // A .slnx that declares no configurations has the ones the solution model supplies by default.
        if (buildTypes.Count == 0) buildTypes = ["Debug", "Release"];
        if (platforms.Count == 0) platforms = ["Any CPU"];

        (string defaultBuildType, string defaultPlatform) = DefaultConfiguration(buildTypes, platforms);

        // Project entries may sit at the root or nested under <Folder> elements, so walk every descendant.
        foreach (XElement project in document.Descendants().Where(element => element.Name.LocalName == "Project"))
        {
            if (project.Attribute("Path")?.Value is not { } path || string.IsNullOrWhiteSpace(path)) continue;

            bool notBuilt = !IsBuilt(project, defaultBuildType, defaultPlatform);
            yield return new SolutionEntry(path, null, IsWebSite: false, notBuilt);
        }
    }

    // Rules apply in document order, and a later match overrides an earlier one.
    private static bool IsBuilt(XElement project, string buildType, string platform)
    {
        var built = true;
        foreach (XElement rule in project.Elements().Where(element => element.Name.LocalName == "Build"))
            if (MatchesConfiguration(rule.Attribute("Solution")?.Value, buildType, platform))
                built = !string.Equals(rule.Attribute("Project")?.Value, "false", StringComparison.OrdinalIgnoreCase);

        return built;
    }

    // A rule's solution pattern is "BuildType|Platform", where "*", an empty half or a missing one matches all.
    private static bool MatchesConfiguration(string? pattern, string buildType, string platform)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;

        string[] halves = pattern.Split('|');
        return MatchesHalf(halves[0], buildType) && (halves.Length < 2 || MatchesHalf(halves[1], platform));
    }

    private static bool MatchesHalf(string half, string value)
    {
        string trimmed = half.Trim();
        return trimmed.Length == 0 || trimmed == "*" || trimmed.Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> NamesOf(IEnumerable<XElement> configurationElements, string elementName)
    {
        return configurationElements
            .SelectMany(element => element.Elements())
            .Where(element => element.Name.LocalName == elementName)
            .Select(element => element.Attribute("Name")?.Value)
            .OfType<string>()
            .Where(name => name.Length > 0)
            .ToList();
    }

    /// <summary>
    ///     The configuration a build of the solution takes when none is named, picked from the build types and
    ///     platforms its configurations use, neither of them empty. This is MSBuild's rule: <c>Debug</c> when any
    ///     configuration has it and the first build type otherwise, then <c>Mixed Platforms</c>, else
    ///     <c>Any CPU</c>, else the first platform.
    /// </summary>
    private static (string BuildType, string Platform) DefaultConfiguration(
        IReadOnlyList<string> buildTypes, IReadOnlyList<string> platforms)
    {
        string buildType = FirstNamed(buildTypes, "Debug") ?? buildTypes[0];
        string platform = FirstNamed(platforms, "Mixed Platforms") ?? FirstNamed(platforms, "Any CPU") ?? platforms[0];
        return (buildType, platform);
    }

    private static string? FirstNamed(IEnumerable<string> names, string wanted)
    {
        return names.FirstOrDefault(name => name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static (string BuildType, string Platform)? SplitConfiguration(string configuration)
    {
        int bar = configuration.IndexOf('|');
        if (bar < 0) return null;

        return (configuration[..bar].Trim(), configuration[(bar + 1)..].Trim());
    }

    // The left-hand side of every assignment in one GlobalSection of a classic .sln, in file order.
    private static IEnumerable<string> SectionKeys(string solutionText, string sectionName)
    {
        foreach (Match section in SlnGlobalSection().Matches(solutionText))
        {
            if (!section.Groups["name"].Value.Trim().Equals(sectionName, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (string line in section.Groups["body"].Value.Split('\n'))
            {
                int equals = line.IndexOf('=');
                if (equals > 0) yield return line[..equals].Trim();
            }
        }
    }

    // The project type a classic .sln gives a Web Site, whose path field holds a folder or a URL.
    private const string WebSiteProjectType = "E24C65DC-7377-472B-9ABA-BC803B73C61A";

    /// <summary>
    ///     One project entry as a solution file declares it, before it is resolved: its raw path and the name a
    ///     classic <c>.sln</c> gives it.
    /// </summary>
    private readonly record struct SolutionEntry(string Path, string? Name, bool IsWebSite, bool NotBuilt);

    /// <summary>
    ///     A classic-<c>.sln</c> project line:
    ///     <c>Project("{TypeGuid}") = "Name", "Relative\Path.csproj", "{ProjectGuid}"</c>, as the named groups
    ///     <c>type</c>, <c>name</c>, <c>path</c> and <c>guid</c>. Solution folders put a folder name in the path
    ///     field, filtered out later by the ends-in-<c>proj</c> entry test.
    /// </summary>
    [GeneratedRegex(
        "Project\\(\"\\{(?<type>[^}]*)\\}\"\\)\\s*=\\s*\"(?<name>[^\"]*)\",\\s*\"(?<path>[^\"]*)\""
        + "(?:\\s*,\\s*\"\\{(?<guid>[^}]*)\\}\")?",
        RegexOptions.CultureInvariant)]
    private static partial Regex SlnProjectLine();

    /// <summary>
    ///     A classic-<c>.sln</c> <c>GlobalSection(Name) = phase</c> block, as the named groups <c>name</c> and
    ///     <c>body</c>, the lines before its <c>EndGlobalSection</c>.
    /// </summary>
    [GeneratedRegex(
        "GlobalSection\\((?<name>[^)]*)\\)[^\\n]*\\n(?<body>.*?)EndGlobalSection",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex SlnGlobalSection();
}
