using SlideRule.Checking;
using SlideRule.Internal;
using SlideRule.ModelFile;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     What a freshly written model file says about the rules in it: how many a reader can settle from one
///     project's compilation, how many take the whole solution, and how many the file could not carry — with
///     a reason beside each rule in the last two groups.
/// </summary>
/// <remarks>
///     <para>
///         Printed under the file's own <c>wrote</c> line and indented beneath it, because it is a fact
///         about that write rather than a second target. An unchanged file prints nothing: the split did
///         not change either, and a report that repeated itself on every run would train its reader to skip
///         the line that matters.
///     </para>
///     <para>
///         The two headers appear only over a group that has rows. A colon promises a block, so a header
///         over nothing reads as truncated output — the rule <c>EvidenceBlock</c> holds its callers to, kept
///         here by the same guard rather than by a shared helper, because these rows are indented lines of a
///         report and not a refusal's evidence.
///     </para>
///     <para>
///         Both reasons are quoted, never composed: the whole-solution one from the classifier and the
///         not-in-the-file one from the document's own omission list. A sentence written here would be a
///         second answer to a question something else has already answered, free to disagree with the file.
///     </para>
/// </remarks>
internal static class ModelSplitReport
{
    /// <summary>The report's lines, already indented, in the order they are printed.</summary>
    /// <param name="document">The document that was just written.</param>
    internal static IReadOnlyList<string> Lines(ModelDocument document)
    {
        List<(string Id, string Reason)> wholeSolution = document.Rules
            .Select(rule => (rule.Id, Verdict: OneProjectDecidability.Classify(rule)))
            .Where(entry => !entry.Verdict.Decidable)
            .Select(entry => (entry.Id, Reason: entry.Verdict.Reason!))
            .ToList();
        List<(string Id, string Reason)> notInTheFile = document.Omitted
            .Select(omitted => (omitted.Id, omitted.Reason))
            .ToList();

        int carried = document.Rules.Count;
        int total = carried + notInTheFile.Count;
        int fromOneProject = carried - wholeSolution.Count;

        var lines = new List<string>
        {
            $"  {Plurals.Counted(total, "rule")}: {fromOneProject} decidable from one project, "
            + $"{wholeSolution.Count} left to the whole solution, {notInTheFile.Count} not in the file"
        };

        AddGroup(lines, "left to the whole solution", wholeSolution);
        AddGroup(lines, "not in the file", notInTheFile);
        return lines;
    }

    private static void AddGroup(List<string> lines, string header, IReadOnlyList<(string Id, string Reason)> rows)
    {
        if (rows.Count == 0) return;

        lines.Add($"  {header}:");
        foreach ((string id, string reason) in rows) lines.Add($"    {id}: {reason}");
    }
}
