namespace SlideRule.Cli.Verbs;

/// <summary>The parsed inputs to a <c>hook</c> run, before anything about the tree it fires for is known.</summary>
/// <remarks>
///     Resolution waits for the run, because the paths below are relative to the working tree the hook fires for,
///     and only the payload says which tree that is. It also keeps a settings file that will not parse inside the
///     hook's own error handling, which reports it without blocking the agent.
/// </remarks>
/// <param name="Solution">
///     The positional solution argument: a file or a directory to search, relative to that tree; or null, for the
///     settings file or the walk up from the tree to name one.
/// </param>
/// <param name="Spec">The <c>--spec</c> value, relative to the same tree, or null for the settings file or the convention.</param>
/// <param name="DiffBase">
///     The <c>--diff-base</c> git ref the check's tripwires list changed files from when the hook hands them no
///     listing of its own, as on the per-edit path.
/// </param>
/// <param name="MaxRounds">
///     The <c>--max-rounds</c> cap: how many consecutive continuations one prompt may get before the hook reports
///     instead of blocking.
/// </param>
/// <param name="WorkingDirectory">
///     The directory the process started in, which stands in for the tree when neither the payload nor the
///     environment names one.
/// </param>
internal sealed record HookRequest(
    string? Solution,
    string? Spec,
    string DiffBase,
    int MaxRounds,
    string WorkingDirectory);
