namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>rename</c> companions: the two options only that subcommand takes, both required by the command
///     tree, so the other three modes carry none. The runner refuses a malformed <see cref="From" /> before any
///     solution work.
/// </summary>
/// <param name="From">
///     The rule ID the baseline section is filed under: the rule's old ID, which the spec no longer declares.
/// </param>
/// <param name="To">The rule's new ID: a ratcheted rule in the spec with no baseline section yet.</param>
internal sealed record BaselineRename(string From, string To);
