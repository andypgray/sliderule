using System.Text;
using SlideRule.Checking;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Renders a <see cref="CheckReport" /> as GitHub Actions workflow commands, one <c>::error</c> or
///     <c>::warning</c> line per <see cref="GitHubFinding" />. A step that prints the file puts each finding on its
///     line of code, with no code-scanning upload behind it.
/// </summary>
internal static class GitHubAnnotationsRenderer
{
    /// <summary>
    ///     Writes the commands for <paramref name="findings" /> to <paramref name="path" />, atomically and
    ///     creating the directory. Throws on a bad path or an I/O failure.
    /// </summary>
    public static void Render(string path, IReadOnlyList<GitHubFinding> findings, string solutionDirectory)
    {
        AtomicFile.WriteAllText(path, Serialize(findings, solutionDirectory));
    }

    /// <summary>The commands for <paramref name="findings" />, one line each, with no filesystem behind them.</summary>
    internal static string Serialize(IReadOnlyList<GitHubFinding> findings, string solutionDirectory)
    {
        var text = new StringBuilder();
        foreach (GitHubFinding finding in findings)
            text.Append(Command(finding, solutionDirectory))
                .Append('\n');

        return text.ToString();
    }

    private static string Command(GitHubFinding finding, string solutionDirectory)
    {
        var properties = new List<string>();
        if (finding.Path is { } path) properties.Add($"file={EscapeProperty(Absolute(solutionDirectory, path))}");
        if (finding.Line is { } line) properties.Add($"line={line}");
        properties.Add($"title={EscapeProperty(finding.RuleId)}");

        return $"::{finding.Level} {string.Join(",", properties)}::{EscapeData(finding.Message)}";
    }

    // The runner makes a file property relative to its workspace once it is absolute, and reads a forward slash as a
    // separator on every platform.
    private static string Absolute(string solutionDirectory, string path)
    {
        string combined = Path.Combine(solutionDirectory, path);
        return Path.GetFullPath(combined)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    // Must agree with @actions/toolkit's escapeData and escapeProperty, which the runner reverses: without them
    // a newline in a message would end the command, and a colon or comma in a path would end the property.
    private static string EscapeData(string value)
    {
        return value.Replace("%", "%25")
            .Replace("\r", "%0D")
            .Replace("\n", "%0A");
    }

    private static string EscapeProperty(string value)
    {
        return EscapeData(value)
            .Replace(":", "%3A")
            .Replace(",", "%2C");
    }
}
