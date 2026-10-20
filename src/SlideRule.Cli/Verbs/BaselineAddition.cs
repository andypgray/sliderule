namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>add</c> companions: the options only that subcommand takes, carried together so the other modes carry
///     none, each null when its option was not given. <paramref name="Source" /> with <paramref name="Target" />
///     names an edge and <paramref name="Subject" /> a shape violation, each by full name or symbol ID.
/// </summary>
internal sealed record BaselineAddition(
    string? Rule,
    string? Because,
    string? Source,
    string? Target,
    string? Subject);
