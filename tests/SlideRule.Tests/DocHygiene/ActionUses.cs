using System.Text.RegularExpressions;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The steps that call an action, read from a workflow, the action metadata or a quoted fence of either.
/// </summary>
internal static class ActionUses
{
    // A step's `uses:` key, as a list item or a mapping key, and the reference after it.
    private static readonly Regex Use = new(@"^\s*(?:-\s+)?uses:\s*['""]?(?<reference>[^\s'""#]+)");

    /// <summary>Every step of the tracked <paramref name="file" /> that calls an action.</summary>
    internal static IReadOnlyList<ActionUse> In(string file)
    {
        return In(file, RepoRoot.ReadLines(file), 1);
    }

    /// <summary>
    ///     Every step in <paramref name="lines" /> that calls an action, numbered from <paramref name="firstLine" />,
    ///     the line of <paramref name="file" /> that <c>lines[0]</c> sits on.
    /// </summary>
    internal static IReadOnlyList<ActionUse> In(string file, string[] lines, int firstLine)
    {
        return Enumerable.Range(0, lines.Length)
            .Select(index => (index, match: Use.Match(lines[index])))
            .Where(static line => line.match.Success)
            .Select(line => new ActionUse(
                file,
                firstLine + line.index,
                line.match.Groups["reference"].Value,
                BodyOf(lines, line.index)))
            .ToArray();
    }

    internal static int IndentOf(string line)
    {
        return line.Length - line.TrimStart().Length;
    }

    private static string[] BodyOf(string[] lines, int index)
    {
        int indent = IndentOf(lines[index]);

        return lines
            .Skip(index + 1)
            .TakeWhile(line => ContinuesStep(line, indent))
            .ToArray();
    }

    // The step's body runs until the next list item, or until a line that dedents out of the block entirely,
    // such as the next job's key. A blank line is neither, so a step whose inputs follow one is still read as one
    // step.
    private static bool ContinuesStep(string line, int indent)
    {
        if (line.Trim().Length == 0) return true;

        if (line.TrimStart().StartsWith("- ", StringComparison.Ordinal)) return false;

        return IndentOf(line) >= indent;
    }
}

/// <summary>
///     A step that calls an action: the file and line of its <c>uses:</c> key, the reference that key names, and the
///     lines of the step below it.
/// </summary>
internal sealed record ActionUse(string File, int Line, string Reference, string[] Body)
{
    // A path into this checkout, such as the self-check job's own action, runs the commit under test.
    public bool IsLocal => Reference.StartsWith("./", StringComparison.Ordinal);
}
