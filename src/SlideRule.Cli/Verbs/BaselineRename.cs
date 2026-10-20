namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>rename</c> companions, both required by the command tree: <paramref name="From" />, the old rule ID a
///     baseline section is still filed under, and <paramref name="To" />, the rule's new ID.
/// </summary>
internal sealed record BaselineRename(string From, string To);
