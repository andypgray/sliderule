using SlideRule.Baselines;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Rendering;

namespace SlideRule.Roslyn.Baselines;

/// <summary>
///     Finds the baseline sections no rule in the spec reads: every section, in every baseline file under
///     <c>arch/baselines/</c> or named by a ratcheted rule, whose rule ID the model does not declare. The usual
///     cause is a rule renamed after capture, whose section stays filed under its old ID.
/// </summary>
/// <remarks>
///     The definition is "names no declared rule", so a section filed under a declared rule's ID in a file that
///     rule does not name is not an orphan. A file the reader refuses, or that cannot be opened, is listed as
///     unreadable and the scan carries on past it: the scan is a nudge, and the refusal that stops a run belongs
///     to the read of a declared file, which happens before the scan is asked for.
/// </remarks>
internal static class BaselineOrphanScanner
{
    // Fixed rather than the exception's message, which names the machine's absolute path: the reason reaches
    // status and status --json, which are otherwise the same on every machine.
    private const string CouldNotOpen = "the file could not be opened.";

    // An inaccessible subdirectory is skipped rather than thrown on: the scan is a nudge, never a reason for the
    // run that asked for it to stop.
    private static readonly EnumerationOptions EveryReachableFile = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true
    };

    /// <summary>
    ///     Scans the baseline files of the solution rooted at <paramref name="solutionDirectory" /> against the
    ///     rule IDs <paramref name="model" /> declares, tripwires and containment children included. Files are
    ///     visited in ordinal order of their solution-relative path, and each file's sections in ordinal order of
    ///     their rule ID. A file found in <paramref name="alreadyRead" />, keyed by its full path under the
    ///     platform's path rule, is listed from that document rather than read again.
    /// </summary>
    internal static BaselineOrphanScan Scan(
        ArchitectureModel model, string solutionDirectory,
        IReadOnlyDictionary<string, BaselineDocument>? alreadyRead = null)
    {
        var declared = new HashSet<string>(model.Rules.Select(rule => rule.Id), StringComparer.Ordinal);
        var orphans = new List<BaselineOrphan>();
        var unreadable = new List<UnreadableBaseline>();

        foreach ((string path, string relative) in Files(model, solutionDirectory))
        {
            BaselineDocument? document =
                alreadyRead is not null && alreadyRead.TryGetValue(path, out BaselineDocument? kept)
                    ? kept
                    : TryRead(path, relative, unreadable);
            if (document is null) continue;

            IEnumerable<KeyValuePair<string, IReadOnlyList<BaselineEntry>>> undeclared = document.Sections
                .Where(section => !declared.Contains(section.Key))
                .OrderBy(section => section.Key, StringComparer.Ordinal);
            foreach (KeyValuePair<string, IReadOnlyList<BaselineEntry>> section in undeclared)
                orphans.Add(new BaselineOrphan(section.Key, relative, section.Value.Count));
        }

        return orphans.Count == 0 && unreadable.Count == 0
            ? BaselineOrphanScan.Empty
            : new BaselineOrphanScan(orphans, unreadable);
    }

    // A file the run has not read already: its document, or null once it is listed as unreadable.
    private static BaselineDocument? TryRead(string path, string relative, ICollection<UnreadableBaseline> unreadable)
    {
        BaselineReadResult result;
        try
        {
            result = BaselineDocumentReader.Read(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreadable.Add(new UnreadableBaseline(relative, CouldNotOpen));
            return null;
        }

        if (result.Refusal is { } refusal)
        {
            unreadable.Add(new UnreadableBaseline(relative, BaselineStore.RefusalDetail(refusal)));
            return null;
        }

        return result.Document!;
    }

    // Every *.json under arch/baselines/ when that directory exists, and every file a ratcheted rule names that
    // exists on disk, once each by the platform's path rule, in ordinal order of the path a report prints. A file
    // is both when a rule names one under arch/baselines/, and read twice its orphans would be listed twice.
    private static IEnumerable<(string Path, string Relative)> Files(ArchitectureModel model, string solutionDirectory)
    {
        string conventionalRoot = Path.Combine(solutionDirectory, BaselineConventions.ConventionalRoot);
        IEnumerable<string> conventional = Directory.Exists(conventionalRoot)
            ? Directory.EnumerateFiles(conventionalRoot, "*.json", EveryReachableFile)
            : [];
        IEnumerable<string> declared = model.Rules
            .Where(rule => rule.BaselinePath is not null)
            .Select(rule => BaselineStore.ResolvePath(rule.BaselinePath!, solutionDirectory))
            .Where(File.Exists);

        return conventional
            .Concat(declared)
            .Select(Path.GetFullPath)
            .Distinct(PathComparison.Comparer)
            .Select(path => (Path: path, Relative: PathFormat.Relative(solutionDirectory, path)))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .ToList();
    }
}
