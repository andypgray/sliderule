using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SlideRule.Tests.TestSupport;

/// <summary>The running test host's own resolved assemblies as metadata references, each read once.</summary>
internal static class TrustedPlatform
{
    private static readonly string ContractPath = typeof(Arch).Assembly.Location;

    // Every assembly the running host resolves against, each read once and kept beside its path. A
    // narrower set is taken by filtering this rather than by reading the files again, so a run that needs
    // several sets pays for the metadata once and Roslyn sees one image per assembly.
    private static readonly ImmutableArray<(string Path, MetadataReference Reference)> Entries = Resolve();

    /// <summary>Every assembly the host resolves against, with exactly one copy of the contract among them.</summary>
    internal static ImmutableArray<MetadataReference> References { get; } = ReferencesOf(Entries);

    /// <summary>The whole set less the contract: what a compilation that brings its own contract builds against.</summary>
    internal static ImmutableArray<MetadataReference> WithoutContract { get; } =
        ReferencesOf(Entries.Where(entry => !IsContract(entry.Path)));

    /// <summary>The whole set less the assemblies named, each by file name.</summary>
    /// <param name="fileNames">The file names to leave out, such as <c>Some.Assembly.dll</c>.</param>
    internal static ImmutableArray<MetadataReference> Less(params string[] fileNames)
    {
        var excluded = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
        IEnumerable<(string Path, MetadataReference Reference)> kept =
            Entries.Where(entry => !excluded.Contains(Path.GetFileName(entry.Path)));

        return ReferencesOf(kept);
    }

    /// <summary>The reference read from <paramref name="path" />, compared as a full path.</summary>
    /// <param name="path">The full path of the assembly.</param>
    internal static MetadataReference At(string path)
    {
        (string Path, MetadataReference Reference) entry =
            Entries.Single(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));

        return entry.Reference;
    }

    // The host's own resolved set, with the contract guaranteed present: a host that had loaded it from
    // somewhere outside that set would otherwise leave every spec compilation with no spec surface to bind
    // against.
    private static ImmutableArray<(string Path, MetadataReference Reference)> Resolve()
    {
        List<string> paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!paths.Any(IsContract)) paths.Add(ContractPath);

        return paths
            .Select(path => (path, (MetadataReference)MetadataReference.CreateFromFile(path)))
            .ToImmutableArray();
    }

    private static bool IsContract(string path)
    {
        return string.Equals(path, ContractPath, StringComparison.OrdinalIgnoreCase);
    }

    private static ImmutableArray<MetadataReference> ReferencesOf(
        IEnumerable<(string Path, MetadataReference Reference)> entries)
    {
        return entries
            .Select(entry => entry.Reference)
            .ToImmutableArray();
    }
}
