using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using SlideRule.Internal;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Cli.SpecLoading;

/// <summary>How a spec source differs from the build that was loaded.</summary>
internal enum StaleReason
{
    /// <summary>The build compiled this file, and its bytes have changed since.</summary>
    Changed,

    /// <summary>The build compiled this file, and it no longer exists.</summary>
    Missing,

    /// <summary>The build did not compile this file, and it was written after the build.</summary>
    Added,

    /// <summary>With no checksums to compare, this file was written after the build.</summary>
    Newer
}

/// <summary>Which evidence a <see cref="StaleSpec" /> verdict rests on.</summary>
internal enum SpecComparison
{
    /// <summary>The document checksums of the build's portable PDB.</summary>
    Checksum,

    /// <summary>File write times, for a build with no portable PDB whose documents can be found on disk.</summary>
    FileTime
}

/// <summary>
///     A spec project whose sources differ from the build the run loaded, so the run checked the rules that build
///     holds: <see cref="DifferingFile" /> is the first differing source in ordinal path order, an absolute path.
/// </summary>
internal sealed record StaleSpec(
    string ProjectName,
    string ProjectFilePath,
    string DifferingFile,
    StaleReason Reason,
    SpecComparison Comparison);

/// <summary>
///     Compares a spec project's sources with the build the run loads, which the CLI never makes itself. A portable
///     PDB records a checksum of every document the build compiled, so where one is embedded in the DLL or sits
///     beside it, a source whose bytes no longer match is exact evidence, unaffected by a checkout, a touch or an
///     edit made and reverted. Without one, a source written after the DLL is the evidence, which can be wrong
///     either way.
/// </summary>
/// <remarks>
///     Sources are the <c>*.cs</c> files the build compiled plus the project directory's default-glob cone
///     (<see cref="ProjectCone.Enumerate" />), less any nested project's directory. A document under a build-output
///     directory, taken relative to the project directory, is generated, and every build writes it again, so it is
///     never compared. A file in the cone that the build did not compile counts only when it is newer than the DLL,
///     so a file a <c>Compile Remove</c> keeps out of the build is not reported for as long as it is not edited; an
///     edit to it is reported until a build that compiles something rewrites the DLL, which is why a nested
///     project's sources are left out. A directory the walk cannot open is skipped, and the rest still count. The
///     project file and its imports are not compared: a PDB does not record them, and a write time is no evidence of
///     a change. Every read shares the file with writers and deleters, so a build running beside the comparison is
///     never blocked by it.
/// </remarks>
internal static class SpecSourceComparison
{
    private static readonly Guid Sha1 = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid Sha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");

    // Hidden and system project files count, as the cone's own walk keeps hidden and system entries.
    private static readonly EnumerationOptions ProjectFileSearch = new() { AttributesToSkip = FileAttributes.None };

    /// <summary>
    ///     The resolved spec's verdict: null when its sources match its build, and always null for a spec passed as a
    ///     DLL, which names no project and so no sources to compare.
    /// </summary>
    internal static StaleSpec? Compare(SpecResolution resolution)
    {
        if (resolution is not { SpecProjectName: { } projectName, ProjectFilePath: { } projectFilePath }) return null;

        return Compare(resolution.DllPath, projectName, projectFilePath);
    }

    /// <summary>The verdict for the build at <paramref name="dllPath" /> of the project file named.</summary>
    internal static StaleSpec? Compare(string dllPath, string projectName, string projectFilePath)
    {
        string? projectDirectory = Path.GetDirectoryName(projectFilePath);
        if (projectDirectory is null || !File.Exists(dllPath)) return null;

        DateTime builtAtUtc = File.GetLastWriteTimeUtc(dllPath);
        IReadOnlyList<string> candidates = CandidateSources(projectDirectory);

        List<PdbDocument>? compiled = ComparableDocuments(dllPath, projectDirectory);
        SpecComparison comparison = compiled is null ? SpecComparison.FileTime : SpecComparison.Checksum;
        (string File, StaleReason Reason)? difference = compiled is null
            ? TimeDifference(candidates, builtAtUtc)
            : ChecksumDifference(compiled, candidates, builtAtUtc);

        return difference is { } found
            ? new StaleSpec(projectName, projectFilePath, found.File, found.Reason, comparison)
            : null;
    }

    // The documents the build compiled from sources, less the generated ones, or null when there is nothing to
    // compare checksums by: no portable PDB, or one none of whose documents resolve on this machine, as in a build
    // that mapped its source paths. A document that resolves nowhere is otherwise a deleted source.
    private static List<PdbDocument>? ComparableDocuments(string dllPath, string projectDirectory)
    {
        IReadOnlyList<PdbDocument>? documents = ReadDocuments(dllPath);
        if (documents is null) return null;

        List<PdbDocument> compiled = documents
            .Where(document => !BuildOutputDirectories.IsUnderBuildOutput(projectDirectory, document.Path))
            .ToList();
        return compiled.Any(document => File.Exists(document.Path)) ? compiled : null;
    }

    private static (string File, StaleReason Reason)? ChecksumDifference(
        IReadOnlyList<PdbDocument> compiled, IReadOnlyList<string> candidates, DateTime builtAtUtc)
    {
        var differences = new List<(string File, StaleReason Reason)>();
        foreach (PdbDocument document in compiled)
            if (!File.Exists(document.Path))
                differences.Add((document.Path, StaleReason.Missing));
            // A document whose checksum cannot be computed here (an unreadable file, an algorithm other than
            // SHA-1 or SHA-256) counts as unchanged, because the warning is raised only on evidence of a change.
            else if (Hash(document.Path, document.Algorithm) is { } actual && !actual.AsSpan().SequenceEqual(document.Checksum))
                differences.Add((document.Path, StaleReason.Changed));

        var compiledPaths = new HashSet<string>(compiled.Select(document => document.Path), PathComparison.Comparer);
        IEnumerable<(string, StaleReason)> added = candidates
            .Where(path => !compiledPaths.Contains(path) && File.GetLastWriteTimeUtc(path) > builtAtUtc)
            .Select(path => (path, StaleReason.Added));
        differences.AddRange(added);

        if (differences.Count == 0) return null;

        return differences.MinBy(difference => difference.File, StringComparer.Ordinal);
    }

    private static (string File, StaleReason Reason)? TimeDifference(
        IReadOnlyList<string> candidates, DateTime builtAtUtc)
    {
        string? newer = candidates
            .Where(path => File.GetLastWriteTimeUtc(path) > builtAtUtc)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        return newer is null ? null : (newer, StaleReason.Newer);
    }

    // The PDB's documents, read from the one embedded in the DLL or the one beside it whose id matches the DLL's,
    // or null when there is no portable PDB to read.
    private static IReadOnlyList<PdbDocument>? ReadDocuments(string dllPath)
    {
        try
        {
            using FileStream dll = OpenShared(dllPath);

            // Prefetching reads the whole image in the constructor and closes the DLL before the PDB lookup opens
            // a second file.
            using var peReader = new PEReader(dll, PEStreamOptions.PrefetchEntireImage);
            if (!peReader.TryOpenAssociatedPortablePdb(dllPath, OpenSharedOrNull, out MetadataReaderProvider? provider, out _)
                || provider is null)
                return null;

            using (provider)
            {
                MetadataReader reader = provider.GetMetadataReader();
                return reader.Documents
                    .Select(handle => reader.GetDocument(handle))
                    .Select(document => new PdbDocument(
                        reader.GetString(document.Name),
                        reader.GetGuid(document.HashAlgorithm),
                        reader.GetBlobBytes(document.Hash)))
                    .ToList();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return null;
        }
    }

    private static byte[]? Hash(string path, Guid algorithm)
    {
        try
        {
            using FileStream stream = OpenShared(path);
            if (algorithm == Sha256) return SHA256.HashData(stream);
            if (algorithm == Sha1) return SHA1.HashData(stream);

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The project directory's cone, less the directories of the projects nested in it.
    private static IReadOnlyList<string> CandidateSources(string projectDirectory)
    {
        var holdsProject = new Dictionary<string, bool>(PathComparison.Comparer);

        return ProjectCone.Enumerate(projectDirectory, skipInaccessible: true)
            .Where(path => !UnderNestedProject(path, projectDirectory, holdsProject))
            .ToList();
    }

    private static bool UnderNestedProject(string path, string projectDirectory, Dictionary<string, bool> holdsProject)
    {
        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory));
        for (string? directory = Path.GetDirectoryName(path);
             directory is not null && !PathComparison.Comparer.Equals(directory, top);
             directory = Path.GetDirectoryName(directory))
        {
            if (!holdsProject.TryGetValue(directory, out bool holds))
            {
                holds = HoldsProject(directory);
                holdsProject[directory] = holds;
            }

            if (holds) return true;
        }

        return false;
    }

    private static bool HoldsProject(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.csproj", ProjectFileSearch).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static FileStream OpenShared(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    private static Stream? OpenSharedOrNull(string path)
    {
        return File.Exists(path) ? OpenShared(path) : null;
    }

    private sealed record PdbDocument(string Path, Guid Algorithm, byte[] Checksum);
}
