namespace SlideRule.Roslyn.Caching;

/// <summary>
///     The build-output directory names (<c>bin</c>, <c>obj</c>), defined once so the on-disk
///     <see cref="ProjectCone">cone scan</see> and the in-memory
///     <see cref="SolutionCacheInputs">document filter</see> cannot drift on what counts as a build artifact.
/// </summary>
/// <remarks>
///     What they share is the names and the comparison rather than one matcher: a disk walk prunes by directory
///     name and never leaves its root, while the document filter reads each path relative to its project
///     (<see cref="IsUnderBuildOutput" />).
/// </remarks>
internal static class BuildOutputDirectories
{
    // Matched ordinally, the safe side for both probes: ignoring case would drop a source file under "BIN" that
    // the build compiles out of the document filter, so its edits would stop invalidating the cache, while the
    // worst case of ordinal is a walk into a directory the build ignored, which can cost a cache miss but never a
    // stale answer.
    private static readonly string[] Names = ["bin", "obj"];

    /// <summary>
    ///     Whether <paramref name="directoryName" /> names a build-output directory. Takes a span so the disk walk
    ///     can ask it of a directory entry without materializing the name.
    /// </summary>
    internal static bool IsBuildOutputName(ReadOnlySpan<char> directoryName)
    {
        foreach (string name in Names)
            if (directoryName.Equals(name, StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>
    ///     True when <paramref name="path" />, taken relative to <paramref name="projectDirectory" />, crosses
    ///     a build-output segment. A path above the project directory (a
    ///     <c>&lt;Compile Include="..\Shared\X.cs"&gt;</c> link) yields a <c>..</c>-prefixed relative path with no
    ///     such segment, so a linked source file outside the cone stays tracked.
    /// </summary>
    /// <remarks>
    ///     Walked as spans over the one relative path it must materialize: it runs once per document per project
    ///     on every cold load, the largest input any cache probe faces.
    /// </remarks>
    internal static bool IsUnderBuildOutput(string projectDirectory, string path)
    {
        ReadOnlySpan<char> remaining = Path.GetRelativePath(projectDirectory, path);
        while (!remaining.IsEmpty)
        {
            int separator = remaining.IndexOfAny('\\', '/');
            ReadOnlySpan<char> segment = separator < 0 ? remaining : remaining[..separator];
            if (IsBuildOutputName(segment)) return true;
            if (separator < 0) break;

            remaining = remaining[(separator + 1)..];
        }

        return false;
    }
}
