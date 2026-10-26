namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The "wrote nothing" oracle: every file under the roots a verb could write to, as path, length and last-write
///     time, so a rewrite in place shows as well as a file added or removed. bin/ and obj/ are left out, because a
///     design-time build writes to them.
/// </summary>
internal static class WriteFootprint
{
    /// <summary>Every file under <paramref name="roots" />, build output aside, described and ordered.</summary>
    internal static string[] Of(params string[] roots)
    {
        return roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => !TempFixtureWorkspace.IsBuildArtifact(Path.GetRelativePath(root, path))))
            .Select(Describe)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string Describe(string path)
    {
        var file = new FileInfo(path);
        return $"{path}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
    }
}
