using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SlideRule.Cli.Rendering;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene.Walkthroughs;

/// <summary>
///     The ways a walkthrough fence shows part of a run rather than all of it, each a pure function of the text
///     the run printed. A fence cut by one of these is compared with the cut exactly, and its page says what was
///     cut, so the lines a fence leaves out are a decision the page states rather than an omission nothing sees.
/// </summary>
/// <remarks>
///     Every input and output is LF, with no trailing newline: the shape <c>RuleBlock</c> returns and the one a
///     fence body is compared in.
/// </remarks>
internal static class FenceCuts
{
    private const string Ellipsis = " …";

    // The line a rule's block or status row starts with: its status word, then its id.
    private static readonly Regex Header = new("^(?:pass|warn|FAIL|skip) (?<rule>[^ ]+)", RegexOptions.CultureInvariant);

    // One violation site under a rule's header: the solution-relative path, the line, then the evidence.
    private static readonly Regex SiteLine = new("^  (?<path>[^ ]+):[0-9]+ — ", RegexOptions.CultureInvariant);

    /// <summary>The report's <c>FAIL</c> blocks in report order: the rules that went red, every line of each.</summary>
    internal static string FailBlocks(string report)
    {
        return string.Join("\n", Blocks(report)
            .Where(static block => block[0]
                .StartsWith("FAIL ", StringComparison.Ordinal))
            .SelectMany(static block => block));
    }

    /// <summary>The <c>FAIL</c> blocks, then the blank line and the summary line the report ends with.</summary>
    internal static string FailBlocksAndSummary(string report)
    {
        return $"{FailBlocks(report)}\n\n{SummaryLine(report)}";
    }

    /// <summary>The report's last line: the one-line summary of what was checked.</summary>
    internal static string SummaryLine(string report)
    {
        return Lines(report)[^1];
    }

    /// <summary>The text without the lines that start with <paramref name="prefix" />.</summary>
    internal static string WithoutLinesStarting(string text, string prefix)
    {
        return string.Join("\n", Lines(text)
            .Where(line => !line.StartsWith(prefix, StringComparison.Ordinal)));
    }

    /// <summary>
    ///     The report with <paramref name="ruleId" />'s site lines cut to those in <paramref name="path" />;
    ///     every other line of the block, and every other block, left as printed.
    /// </summary>
    internal static string KeepingSitesIn(string report, string ruleId, string path)
    {
        List<string> kept = [];
        var inRule = false;
        foreach (string line in Lines(report))
        {
            if (!line.StartsWith(' ')) inRule = HeaderRuleId(line) == ruleId;

            bool foreignSite = inRule && SiteLine.Match(line) is { Success: true } site && site.Groups["path"].Value != path;
            if (!foreignSite) kept.Add(line);
        }

        return string.Join("\n", kept);
    }

    /// <summary>The one line of a <c>status</c> board that reports <paramref name="ruleId" />.</summary>
    internal static string StatusLine(IReadOnlyList<string> board, string ruleId)
    {
        List<string> lines = board.Where(row => HeaderRuleId(row) == ruleId)
            .ToList();

        return lines is [var line]
            ? line
            : throw new InvalidOperationException($"The status board reports '{ruleId}' on {lines.Count} lines, where a status fence quotes one.");
    }

    /// <summary>
    ///     <paramref name="output" /> as a page's transcript prints it: the command that was run, what it printed,
    ///     and the exit code it returned.
    /// </summary>
    internal static string Transcript(string command, string output, int exitCode)
    {
        return $"$ {command}\n{output}\nexit: {exitCode}";
    }

    /// <summary>
    ///     A <c>graph --json</c> document with its <c>externalEdges</c> cut to the rows <paramref name="kept" />
    ///     names, each by its source project and target namespace root, re-serialized as the verb writes it.
    /// </summary>
    internal static string KeepingExternalEdges(string document, params (string Source, string Root)[] kept)
    {
        JsonNode survey = JsonNode.Parse(document)!;
        JsonArray edges = survey["externalEdges"]!.AsArray();
        edges.RemoveAll(edge => edge is not null
                                && !kept.Contains((edge["source"]!.GetValue<string>(), edge["targetNamespaceRoot"]!.GetValue<string>())));

        return survey.ToJsonString(SlideRuleJson.Options);
    }

    /// <summary>
    ///     A hook document with its <c>additionalContext</c> cut where the quoted <paramref name="fence" /> cuts it:
    ///     at the <c> …</c> the fence's context ends with, after as many characters as the fence keeps.
    /// </summary>
    /// <remarks>
    ///     What the fence keeps must be the run's own prefix; where it is not, the run cut at the same length is
    ///     what the red prints. With no fence to read the cut from, the document comes back whole.
    /// </remarks>
    internal static string ContextCutAsQuoted(string document, IReadOnlyList<string> fence)
    {
        if (fence.Count == 0) return document;

        string quoted = AdditionalContext(JsonNode.Parse(string.Join("\n", fence))!)
            .NormalizedLines();
        if (!quoted.EndsWith(Ellipsis, StringComparison.Ordinal))
            throw new InvalidOperationException($"The quoted hook document's context does not end with '{Ellipsis}', which is where it declares its cut.");

        JsonNode run = JsonNode.Parse(document)!;
        string context = AdditionalContext(run);
        int kept = Math.Min(quoted.Length - Ellipsis.Length, context.Length);

        run["hookSpecificOutput"]!["additionalContext"] = context[..kept] + Ellipsis;
        return run.ToJsonString(SlideRuleJson.Options);
    }

    /// <summary><paramref name="text" /> with CRLF folded to LF.</summary>
    internal static string Folded(string text)
    {
        return text.NormalizedLines();
    }

    private static string AdditionalContext(JsonNode document)
    {
        return document["hookSpecificOutput"]?["additionalContext"]?.GetValue<string>()
               ?? throw new InvalidOperationException("The hook document carries no hookSpecificOutput.additionalContext.");
    }

    // The report as its rule blocks: each a header line and the indented lines under it. The blank line and the
    // summary that end a report head no block.
    private static IEnumerable<List<string>> Blocks(string report)
    {
        List<string>? block = null;
        foreach (string line in Lines(report))
        {
            if (line.StartsWith(' ') && block is not null)
            {
                block.Add(line);
                continue;
            }

            if (block is not null) yield return block;

            block = HeaderRuleId(line) is null ? null : [line];
        }

        if (block is not null) yield return block;
    }

    // The rule a report or status line heads, or null for a line that heads none.
    private static string? HeaderRuleId(string line)
    {
        Match header = Header.Match(line);
        return header.Success ? header.Groups["rule"].Value : null;
    }

    private static string[] Lines(string text)
    {
        return text.NormalizedLines()
            .Split('\n');
    }
}
