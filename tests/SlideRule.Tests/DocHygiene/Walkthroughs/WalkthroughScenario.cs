using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene.Walkthroughs;

/// <summary>How a <see cref="SourceEdit" /> changes the text at its anchor.</summary>
internal enum EditKind
{
    /// <summary>The anchor is replaced by the edit's text.</summary>
    Replace,

    /// <summary>The edit's text goes in immediately before the anchor.</summary>
    InsertBefore,

    /// <summary>The edit's text goes in immediately after the anchor.</summary>
    InsertAfter,

    /// <summary>The anchor is removed.</summary>
    Delete
}

/// <summary>
///     One change a walkthrough makes to a committed file of its example: the file at <paramref name="Path" />,
///     relative to the example's directory, changed by <paramref name="Kind" /> at the one place
///     <paramref name="Anchor" /> occurs, with <paramref name="Text" /> as what goes in.
/// </summary>
/// <remarks>
///     Located by text rather than by line, so an edit to the committed file that leaves the anchor alone leaves
///     the scenario alone, and one that moves the anchor's text reds here rather than landing the change
///     somewhere the page never meant.
/// </remarks>
internal sealed record SourceEdit(string Path, string Anchor, EditKind Kind, string Text = "")
{
    /// <summary>
    ///     <paramref name="text" /> with this edit made, or an exception naming <paramref name="scenario" />
    ///     when the anchor does not occur exactly once.
    /// </summary>
    internal string ApplyTo(string text, string scenario)
    {
        int occurrences = TextNormalization.Occurrences(text, Anchor);
        if (occurrences != 1)
            throw new InvalidOperationException(
                $"Scenario '{scenario}' anchors an edit to {Path} on text that occurs {occurrences} times there, "
                + $"where it must occur exactly once:\n{Anchor}");

        int at = text.IndexOf(Anchor, StringComparison.Ordinal);
        string before = text[..at];
        string after = text[(at + Anchor.Length)..];

        return Kind switch
        {
            EditKind.Replace => before + Text + after,
            EditKind.InsertBefore => before + Text + Anchor + after,
            EditKind.InsertAfter => before + Anchor + Text + after,
            EditKind.Delete => before + after,
            _ => throw new InvalidOperationException($"Scenario '{scenario}' names an edit kind this type does not know: {Kind}.")
        };
    }
}

/// <summary>
///     A file a walkthrough adds to its example that the tree does not hold: its <paramref name="Path" />,
///     relative to the example's directory, and its whole <paramref name="Source" />.
/// </summary>
internal sealed record ImaginedFile(string Path, string Source);

/// <summary>
///     One edit a walkthrough page asks its reader to imagine: the <paramref name="Example" /> it is made to,
///     the <paramref name="Edits" /> to committed files and the files <paramref name="Added" /> beside them,
///     applied on top of the scenario it continues (<paramref name="Over" />) where it has one. The rules the
///     page says the edit breaks are <paramref name="Moves" />, and the rules it says warn under a diff-aware
///     run are <paramref name="Warns" />.
/// </summary>
internal sealed record WalkthroughScenario(
    string Key,
    string Example,
    IReadOnlyList<SourceEdit> Edits,
    IReadOnlyList<ImaginedFile> Added,
    IReadOnlyList<string> Moves,
    IReadOnlyList<string> Warns,
    WalkthroughScenario? Over = null)
{
    /// <summary>
    ///     The text of every file the scenario changes or adds, keyed by its example-relative path, read out of
    ///     <paramref name="readCommitted" />, which answers null for a file the tree does not hold.
    /// </summary>
    /// <remarks>
    ///     Pure: nothing is written, so the same call serves the replay, which compiles the result, and the
    ///     gates that need no build. An anchor that is missing or not unique, an edit to a file that is not
    ///     committed, and an imagined file that is, each throw naming this scenario.
    /// </remarks>
    internal IReadOnlyDictionary<string, string> Apply(Func<string, string?> readCommitted)
    {
        Dictionary<string, string> edited = Over is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(Over.Apply(readCommitted), StringComparer.Ordinal);

        foreach (SourceEdit edit in Edits)
        {
            string text = edited.TryGetValue(edit.Path, out string? earlier)
                ? earlier
                : readCommitted(edit.Path)
                  ?? throw new InvalidOperationException($"Scenario '{Key}' edits {edit.Path}, which {Example} does not hold.");

            edited[edit.Path] = edit.ApplyTo(text, Key);
        }

        foreach (ImaginedFile file in Added)
        {
            if (readCommitted(file.Path) is not null)
                throw new InvalidOperationException(
                    $"Scenario '{Key}' imagines {file.Path}, which {Example} now holds; the page's edit has landed, "
                    + "so its output is no longer a scenario.");

            edited[file.Path] = file.Source;
        }

        return edited;
    }

    /// <summary>The example-relative paths of the files this scenario adds, the ones it continues included.</summary>
    internal IReadOnlySet<string> AddedPaths =>
        Added.Select(static file => file.Path)
            .Concat(Over?.AddedPaths ?? Enumerable.Empty<string>())
            .ToHashSet(StringComparer.Ordinal);
}
