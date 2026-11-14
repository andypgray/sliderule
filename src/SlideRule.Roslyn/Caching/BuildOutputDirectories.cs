using SlideRule.Internal;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Caching;

/// <summary>
///     Which directories are build output, decided once so the on-disk <see cref="ProjectCone">cone scan</see> and
///     the in-memory <see cref="SolutionCacheInputs">document filter</see> cannot drift on what counts as a build
///     artifact. A directory named <c>bin</c> or <c>obj</c> is build output when the directory holding it holds a
///     project file, the project's own or a nested project's; any other <c>bin</c> or <c>obj</c> holds sources, as
///     the SDK's default compile glob reads it.
/// </summary>
/// <remarks>
///     <para>
///         An instance memoises the project-file probe per parent directory, so one operation (one cone walk, one
///         collection of a solution's inputs) lists each parent once however many documents sit under it. Hold one
///         for one operation only: a project file added later is invisible to a memo that already answered.
///     </para>
///     <para>
///         A nested project's own <c>bin</c> and <c>obj</c> count as build output although the parent's compile glob
///         excludes only the parent's output roots, because their generated files change on every nested build and
///         tracking them would miss the cache each time.
///     </para>
/// </remarks>
internal sealed class BuildOutputDirectories
{
    // Matched ordinally, the safe side for both probes: ignoring case would drop a source file under "BIN" that
    // the build compiles out of the document filter, so its edits would stop invalidating the cache, while the
    // worst case of ordinal is a walk into a directory the build ignored, which can cost a cache miss but never a
    // stale answer.
    private static readonly string[] Names = ["bin", "obj"];

    // Hidden and system project files count, as the cone's own walk keeps hidden and system entries.
    private static readonly EnumerationOptions ProjectFileSearch = new() { AttributesToSkip = FileAttributes.None };

    private readonly Dictionary<string, bool> holdsProjectFile = new(PathComparison.Comparer);

    /// <summary>
    ///     Whether the directory <paramref name="directoryName" />, in <paramref name="parentDirectory" />, is build
    ///     output. Takes both as spans so the disk walk can ask it of every directory entry and materialize the parent
    ///     only beside a <c>bin</c> or <c>obj</c>.
    /// </summary>
    internal bool IsBuildOutputDirectory(ReadOnlySpan<char> directoryName, ReadOnlySpan<char> parentDirectory)
    {
        return IsBuildOutputName(directoryName) && HoldsProjectFile(parentDirectory.ToString());
    }

    /// <summary>
    ///     True when <paramref name="path" />, taken relative to <paramref name="projectDirectory" />, crosses a
    ///     build-output directory. Below the project directory that is <see cref="IsBuildOutputDirectory" />. A path
    ///     outside it (a <c>&lt;Compile Include="..\Shared\X.cs"&gt;</c> link, or a generated file under an artifacts
    ///     layout's <c>obj</c>) counts as build output on any <c>bin</c> or <c>obj</c> segment, since no cone walk
    ///     reaches it, and one with no such segment stays tracked.
    /// </summary>
    /// <remarks>
    ///     Walked as spans over the one relative path it must materialize: it runs once per document per project
    ///     on every cold load, the largest input any cache probe faces.
    /// </remarks>
    internal bool IsUnderBuildOutput(string projectDirectory, string path)
    {
        string relative = Path.GetRelativePath(projectDirectory, path);
        bool outside = Path.IsPathRooted(relative) || IsParentSegment(relative);

        ReadOnlySpan<char> remaining = relative;
        var consumed = 0;
        while (true)
        {
            int separator = remaining.IndexOfAny('\\', '/');
            if (separator < 0) return false;

            ReadOnlySpan<char> segment = remaining[..separator];
            if (IsBuildOutputName(segment)
                && (outside || HoldsProjectFile(ParentOf(projectDirectory, relative, consumed))))
                return true;

            consumed += separator + 1;
            remaining = remaining[(separator + 1)..];
        }
    }

    // The directory holding the segment that starts at consumed in relative.
    private static string ParentOf(string projectDirectory, string relative, int consumed)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory));
        return consumed == 0 ? root : Path.Join(root, relative.AsSpan(0, consumed - 1));
    }

    private static bool IsParentSegment(string relative)
    {
        return relative.StartsWith("..", StringComparison.Ordinal)
               && (relative.Length == 2 || relative[2] is '\\' or '/');
    }

    private static bool IsBuildOutputName(ReadOnlySpan<char> directoryName)
    {
        foreach (string name in Names)
            if (directoryName.Equals(name, StringComparison.Ordinal))
                return true;

        return false;
    }

    // A directory that is gone (a document deleted since the load) or that cannot be listed, which the search
    // options skip, holds no project file as far as this probe knows, so what is under it stays a source: a cache
    // miss is the worst that follows.
    private bool HoldsProjectFile(string directory)
    {
        if (holdsProjectFile.TryGetValue(directory, out bool holds)) return holds;

        try
        {
            holds = Directory.EnumerateFiles(directory, "*", ProjectFileSearch)
                .Any(SolutionProjectFileParser.IsProjectEntry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            holds = false;
        }

        holdsProjectFile[directory] = holds;
        return holds;
    }
}
