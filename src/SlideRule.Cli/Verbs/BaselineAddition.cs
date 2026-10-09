namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>add</c> companions: the options only that subcommand takes, carried together so the other three modes
///     carry none. Each is null when its option was not given; the runner validates them together before any
///     workspace cost.
/// </summary>
/// <param name="Rule">The ratcheted rule ID the entry joins, or null when <c>--rule</c> was not given.</param>
/// <param name="Because">
///     The mandatory single-line attribution recorded on the entry, or null when <c>--because</c> was not given.
/// </param>
/// <param name="Source">
///     The edge's referencing type — a full type name or <c>T:</c> symbol ID — for the edge form of <c>add</c>.
/// </param>
/// <param name="Target">
///     The edge's referenced type, or a banned member's full name or member symbol ID, for the edge form of
///     <c>add</c>.
/// </param>
/// <param name="Subject">
///     The offending type or member — a full name or symbol ID — for the shape form of <c>add</c>.
/// </param>
internal sealed record BaselineAddition(
    string? Rule,
    string? Because,
    string? Source,
    string? Target,
    string? Subject);
