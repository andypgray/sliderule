using SlideRule.Internal;

namespace SlideRule.Checking;

/// <summary>
///     The files a check is to treat as changed, which a scope's tripwire warns from, and the solution directory
///     its warnings name them relative to. Paths may arrive with either separator and are compared under
///     <see cref="PathComparison" />'s per-OS rule.
/// </summary>
// Pure string logic: no Path.GetRelativePath and no Span, neither being available on netstandard2.0.
internal sealed class DiffContext
{
    private readonly HashSet<string> _changed;
    private readonly string _solutionPrefix;

    public DiffContext(string solutionDirectory, IEnumerable<string> changedFiles)
    {
        string normalizedDirectory = Normalize(Guard.NotNull(solutionDirectory, nameof(solutionDirectory)));
        _solutionPrefix = normalizedDirectory.TrimEnd('/') + "/";
        _changed = new HashSet<string>(PathComparison.Comparer);
        foreach (string file in Guard.NotNull(changedFiles, nameof(changedFiles))) _changed.Add(Normalize(file));
    }

    internal bool Contains(string filePath)
    {
        return _changed.Contains(Normalize(filePath));
    }

    /// <summary>
    ///     The solution-relative, forward-slash form of <paramref name="filePath" />, or the normalized path
    ///     unchanged where it is not under the solution directory.
    /// </summary>
    internal string SolutionRelative(string filePath)
    {
        string normalized = Normalize(filePath);
        return normalized.StartsWith(_solutionPrefix, PathComparison.Comparison)
            ? normalized.Substring(_solutionPrefix.Length)
            : normalized;
    }

    // Backslashes → forward slashes; no case folding here (the set and SolutionRelative both compare
    // with the per-OS PathComparison), so the stored path keeps its original casing.
    private static string Normalize(string path)
    {
        return path.Replace('\\', '/');
    }
}
