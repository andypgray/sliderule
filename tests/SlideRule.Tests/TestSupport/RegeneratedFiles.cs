namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Where a drift gate leaves the file it regenerated, so the red can name a file to compare and copy
///     rather than paste the whole of it into the failure message.
/// </summary>
internal static class RegeneratedFiles
{
    /// <summary>
    ///     Writes <paramref name="content" /> beside the test assembly as <c>&lt;file name&gt;.actual</c> and
    ///     returns the absolute path.
    /// </summary>
    /// <remarks>
    ///     Never writes over the committed file: the gates that call this never self-rewrite, so a person reads
    ///     every changed line before copying it across. The name is the committed file's own name with
    ///     <c>.actual</c> appended, so the two sort together in a directory listing. Only the file name is kept
    ///     from <paramref name="repoRelativePath" />, deliberately: a path with directories in it, such as the
    ///     served prompt's under <c>src/</c>, still lands flat beside the assembly rather than in a mirrored
    ///     tree nobody looks in.
    /// </remarks>
    internal static string Write(string repoRelativePath, string content)
    {
        string fileName = Path.GetFileName(repoRelativePath);
        string path = Path.Combine(AppContext.BaseDirectory, $"{fileName}.actual");
        File.WriteAllText(path, content);

        return path;
    }

    /// <summary>
    ///     The lines a drift gate reports for <paramref name="repoRelativePath" />: each of <paramref name="moved" />
    ///     under the path, then the copy that would land <paramref name="regenerated" />, which this writes beside
    ///     the test assembly through <see cref="Write" />.
    /// </summary>
    internal static IReadOnlyList<string> DriftReport(string repoRelativePath, IEnumerable<string> moved, string regenerated)
    {
        string actual = Write(repoRelativePath, regenerated);
        string committed = RepoRoot.Absolute(repoRelativePath);
        List<string> report = moved.Select(finding => $"{repoRelativePath}  {finding}")
            .ToList();
        report.Add($"{repoRelativePath}  once every line above is intended: cp '{actual}' '{committed}'");

        return report;
    }
}
