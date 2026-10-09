using SlideRule.Rendering;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The lines a verb reports a target file on: <see cref="Line" /> for a write, and <see cref="CheckLine" />
///     for a <c>render --check</c> comparison. Their words and the solution-relative path are pinned stdout, so
///     the format is owned here rather than restated by each runner that writes a file.
/// </summary>
internal static class WriteReport
{
    /// <summary>
    ///     The report line for <paramref name="path" />: <c>wrote</c> when the bytes changed,
    ///     <c>unchanged</c> when they did not, then the path relative to
    ///     <paramref name="solutionDirectory" /> so the report reads the same on any machine.
    /// </summary>
    internal static string Line(WriteOutcome outcome, string solutionDirectory, string path)
    {
        string label = outcome == WriteOutcome.Wrote ? "wrote" : "unchanged";
        return $"{label} {PathFormat.Relative(solutionDirectory, path)}";
    }

    /// <summary>
    ///     A <c>render --check</c> line: <c>current</c> where the file already holds what a render would write,
    ///     <c>stale</c> where a render would change it.
    /// </summary>
    internal static string CheckLine(bool current, string solutionDirectory, string path)
    {
        string label = current ? "current" : "stale";
        return $"{label} {PathFormat.Relative(solutionDirectory, path)}";
    }
}
