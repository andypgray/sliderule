namespace SlideRule.Cli.Verbs;

/// <summary>
///     The exit codes the CLI and the MCP server return, except <c>sliderule hook</c>, which answers in
///     Claude Code's codes (<see cref="SlideRule.Cli.Hooks.HookExitCodes" />). A run that outlives
///     System.CommandLine's termination grace after Ctrl+C or a termination signal exits 130 or 143 instead;
///     System.CommandLine sets those, not this tool.
/// </summary>
internal static class ExitCodes
{
    /// <summary>The run succeeded: <c>check</c> found no red rule, or a verb that never judges finished.</summary>
    public const int Clean = 0;

    /// <summary>
    ///     <c>check</c> found a red rule, or <c>render --check</c> found a file a render would change; no other verb
    ///     returns it.
    /// </summary>
    public const int Violations = 1;

    /// <summary>Everything else: a usage or parse error, a refused run, an incomplete model, or a bug.</summary>
    public const int Error = 2;
}
