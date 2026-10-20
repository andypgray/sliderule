using SlideRule.Internal;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     <see cref="PathCanonicalizer.Resolve" /> over the project files of one solution walk, with the
///     ancestor probe paid once per directory instead of once per project.
/// </summary>
/// <remarks>
///     <para>
///         Canonicalizing probes the filesystem for a reparse point at every path segment, and the projects of
///         one solution share nearly all of theirs, so resolving each <c>.csproj</c> from scratch turns a large
///         solution into hundreds of syscalls for an answer that barely varies. The directory half is memoized
///         and the leaf name reattached.
///     </para>
///     <para>
///         When the leaf is itself a reparse point the full resolve runs anyway: a <c>.csproj</c> that is a
///         symlink follows through <see cref="System.IO.FileSystemInfo.ResolveLinkTarget" /> exactly as a
///         directory does (measured), so reattaching the leaf name would answer with the link's own path where
///         <see cref="PathCanonicalizer.Resolve" /> answers with its target's. Both sides of a declared-membership
///         test have to agree on one spelling or a declared project reads as a passenger.
///     </para>
///     <para>
///         One instance per walk, never shared and never static: the memo records what the filesystem said at
///         one instant, and a memo that outlived a walk would answer for a tree that has since moved.
///     </para>
/// </remarks>
internal sealed class ProjectFileCanonicalizer
{
    private readonly Dictionary<string, string> resolvedDirectories = new(PathComparison.Comparer);

    /// <summary>
    ///     The canonical spelling of <paramref name="projectFilePath" />, byte for byte what
    ///     <see cref="PathCanonicalizer.Resolve" /> returns for it, or <see langword="null" /> where the
    ///     workspace reported no project file at all, which never matches.
    /// </summary>
    internal string? Resolve(string? projectFilePath)
    {
        if (string.IsNullOrEmpty(projectFilePath)) return null;

        // A path with no directory part has nothing to memoize, and resolving the whole of it is also what
        // makes it absolute.
        string? directory = Path.GetDirectoryName(projectFilePath);
        if (string.IsNullOrEmpty(directory)) return PathCanonicalizer.Resolve(projectFilePath);

        if (!resolvedDirectories.TryGetValue(directory, out string? resolvedDirectory))
        {
            resolvedDirectory = PathCanonicalizer.Resolve(directory);
            resolvedDirectories[directory] = resolvedDirectory;
        }

        string reattached = Path.Combine(resolvedDirectory, Path.GetFileName(projectFilePath));
        return PathCanonicalizer.IsLink(reattached) ? PathCanonicalizer.Resolve(projectFilePath) : reattached;
    }
}
