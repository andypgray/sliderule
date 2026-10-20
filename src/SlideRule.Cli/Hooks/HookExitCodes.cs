namespace SlideRule.Cli.Hooks;

/// <summary>
///     The exit codes <c>sliderule hook</c> returns. They are Claude Code's, not the CLI's, and the two number
///     the same outcomes differently. The meanings below are Claude Code's own (verified 2026-10-20 on Claude Code
///     2.1.300).
/// </summary>
/// <remarks>
///     The CLI's own codes are 0 clean, 1 a red rule and 2 its own error. Handed to Claude Code unchanged, a red
///     rule would pass as a non-blocking error and a misconfiguration would block the agent, which then tries to
///     "fix" code that is not wrong. So 2 here means a red rule and nothing else.
/// </remarks>
internal static class HookExitCodes
{
    /// <summary>
    ///     The hook passes: a stop stands, a prompt goes through, an edit is accepted. Claude Code reads stdout as a
    ///     hook document when it is one JSON object, and otherwise only logs it.
    /// </summary>
    public const int Proceed = 0;

    /// <summary>
    ///     A non-blocking error: Claude Code records stderr as a hook-error notice the user can open and carries on
    ///     as if the hook had passed. Stdout must then hold no hook document, because a valid one decides instead of
    ///     the exit code. SlideRule's own failure exits with it, and so does the round cap's last report.
    /// </summary>
    public const int Report = 1;

    /// <summary>
    ///     A blocking error: Claude Code refuses the stop and hands stderr to the agent as its reason to keep working.
    /// </summary>
    public const int Block = 2;
}
