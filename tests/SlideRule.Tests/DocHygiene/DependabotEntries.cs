using System.Text.RegularExpressions;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>The Dependabot configuration's nuget update entries, read by scan for the gates that hold them.</summary>
internal static class DependabotEntries
{
    /// <summary>The configuration's repository-relative path.</summary>
    internal const string ConfigPath = ".github/dependabot.yml";

    private static readonly Regex EcosystemEntry = new(@"^\s*-\s*package-ecosystem:\s*(?<name>\S+)\s*$");

    private static readonly Regex DirectoriesKey = new(@"^\s*directories:\s*$");

    private static readonly Regex DirectoryItem = new(@"^\s*-\s*(?<path>/\S*)\s*$");

    private static readonly Regex IgnoreKey = new(@"^\s*ignore:\s*$");

    private static readonly Regex IgnoredName =
        new(@"^\s*-?\s*dependency-name:\s*[""']?(?<name>[^""'\s]+)[""']?\s*$");

    private static readonly Regex IgnoredVersions = new(@"^\s*versions:\s*(?<value>\S.*?)\s*$");

    /// <summary>
    ///     The lines of each nuget update entry in file order, from its own <c>- package-ecosystem:</c> line
    ///     to the line before the next entry of any ecosystem. Throws where the configuration carries none, so
    ///     no gate that reads it can pass over nothing.
    /// </summary>
    internal static IReadOnlyList<string[]> NuGetEntries()
    {
        string[] lines = RepoRoot.ReadLines(ConfigPath);
        List<string[]> entries = new();
        for (var index = 0; index < lines.Length; index++)
        {
            if (!IsEcosystem(lines[index], "nuget")) continue;

            int next = Array.FindIndex(lines, index + 1, EcosystemEntry.IsMatch);
            int end = next < 0 ? lines.Length : next;
            entries.Add(lines[index..end]);
        }

        if (entries.Count == 0)
            throw new InvalidOperationException(
                $"{ConfigPath} carries no nuget update entry, so nothing keeps the pinned package versions current.");

        return entries;
    }

    /// <summary>
    ///     The directories one update entry lists: the bullet list under its <c>directories:</c> key, which
    ///     ends at the first line that is not one of its items.
    /// </summary>
    internal static IReadOnlyList<string> Directories(string[] entry)
    {
        int keyIndex = Array.FindIndex(entry, DirectoriesKey.IsMatch);
        if (keyIndex < 0) return [];

        List<string> directories = new();
        for (int index = keyIndex + 1; index < entry.Length; index++)
        {
            Match item = DirectoryItem.Match(entry[index]);
            if (!item.Success) break;

            directories.Add(item.Groups["path"].Value);
        }

        return directories;
    }

    /// <summary>
    ///     The dependency names one update entry's <c>ignore:</c> list carries. The list ends at the first
    ///     line indented no deeper than its key that is not one of its items, so a later key's list (an
    ///     <c>allow:</c>, whose items are spelled the same way) is never read as an ignore.
    /// </summary>
    internal static IReadOnlyList<string> IgnoredNames(string[] entry)
    {
        return Ignores(entry)
            .Select(static ignore => ignore.Name)
            .ToList();
    }

    /// <summary>
    ///     Each ignore one update entry's <c>ignore:</c> list carries, read over the same span as
    ///     <see cref="IgnoredNames" />: the dependency name, and the text of its <c>versions:</c> key as written,
    ///     or <see langword="null" /> where the ignore holds every version.
    /// </summary>
    internal static IReadOnlyList<DependabotIgnore> Ignores(string[] entry)
    {
        int keyIndex = Array.FindIndex(entry, IgnoreKey.IsMatch);
        if (keyIndex < 0) return [];

        int keyIndent = IndentOf(entry[keyIndex]);
        List<DependabotIgnore> ignores = new();
        for (int index = keyIndex + 1; index < entry.Length; index++)
        {
            string line = entry[index];
            string content = line.TrimStart();
            if (content.Length == 0 || content.StartsWith('#')) continue;

            if (IndentOf(line) <= keyIndent && !content.StartsWith('-')) break;

            Match name = IgnoredName.Match(line);
            if (name.Success)
            {
                ignores.Add(new DependabotIgnore(name.Groups["name"].Value, null));
                continue;
            }

            Match versions = IgnoredVersions.Match(line);
            if (versions.Success && ignores.Count > 0)
                ignores[^1] = ignores[^1] with { Versions = versions.Groups["value"].Value };
        }

        return ignores;
    }

    /// <summary>
    ///     One finding per nuget update entry and package of <paramref name="packages" /> that the entry's
    ///     <c>ignore:</c> list does not name exactly, ending in <paramref name="consequence" />, what that entry's
    ///     next update run does.
    /// </summary>
    internal static IReadOnlyList<string> MissingIgnores(IReadOnlyList<string> packages, string consequence)
    {
        List<string> findings = new();
        foreach (string[] entry in NuGetEntries())
        {
            IReadOnlyList<string> ignored = IgnoredNames(entry);
            string directories = string.Join(", ", Directories(entry));
            findings.AddRange(packages
                .Where(package => !ignored.Contains(package, StringComparer.Ordinal))
                .Select(package => $"{ConfigPath}'s nuget entry for {directories} carries no 'ignore:' naming "
                                   + $"{package}, so its next update run {consequence}."));
        }

        return findings;
    }

    private static int IndentOf(string line)
    {
        return line.Length - line.TrimStart(' ').Length;
    }

    private static bool IsEcosystem(string line, string name)
    {
        Match entry = EcosystemEntry.Match(line);

        return entry.Success && entry.Groups["name"].Value == name;
    }
}

/// <summary>
///     One ignore of an update entry: the dependency it names (<paramref name="Name" />), and the text of its
///     <c>versions:</c> key as written (<paramref name="Versions" />), or <see langword="null" /> where it holds
///     every version.
/// </summary>
internal sealed record DependabotIgnore(string Name, string? Versions);
