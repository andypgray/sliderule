using SlideRule.Roslyn.MsBuild;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     The shape every refusal, caveat and stamp in this repo states its evidence in: a lede, the evidence
///     two-space-indented beneath it, then what to do about it.
/// </summary>
/// <remarks>
///     The block is LF-joined whatever the platform, so it is one comparable value wherever it was built;
///     <see cref="LineBlocks" /> writes it back as console lines.
/// </remarks>
internal static class EvidenceBlock
{
    /// <summary>
    ///     The composed block: <paramref name="lede" />, then <paramref name="evidence" /> indented two spaces, then
    ///     the MSBuild selection note inside that indented run, then <paramref name="tail" />, the remedy or the
    ///     reading being denied.
    /// </summary>
    /// <remarks>
    ///     <paramref name="quoteCap" /> quotes that many entries and counts the rest: a refusal nobody reads to the
    ///     end names nothing, and a project list the reader acts on entry by entry goes uncapped.
    ///     <paramref name="withSelectionNote" /> is for a refusal carrying its own evidence, since an MCP surface has
    ///     no stderr echo above it to read the selection from.
    /// </remarks>
    /// <exception cref="ArgumentException">
    ///     <paramref name="lede" /> ends in a colon and <paramref name="evidence" /> is empty: a colon answered by
    ///     the remedy line reads as truncated output, so a caller whose list can be empty guards the whole block on
    ///     it or ends the lede in a full stop.
    /// </exception>
    internal static string Compose(
        string lede,
        IReadOnlyList<string> evidence,
        string? tail = null,
        bool withSelectionNote = false,
        int? quoteCap = null)
    {
        if (evidence.Count == 0 && lede.EndsWith(':'))
            throw new ArgumentException(
                $"A lede ending in a colon promises the block beneath it, and there is none: '{lede}'. "
                + "Guard the whole block on the list, or end the lede in a full stop.",
                nameof(evidence));

        int quoted = quoteCap ?? evidence.Count;

        var lines = new List<string> { lede };
        lines.AddRange(evidence.Take(quoted).Select(entry => "  " + entry));
        if (evidence.Count > quoted) lines.Add($"  ... and {evidence.Count - quoted} more.");
        if (withSelectionNote) lines.Add("  " + MsBuildBootstrap.SelectionNote());
        if (tail is not null) lines.Add(tail);

        return string.Join("\n", lines);
    }
}
