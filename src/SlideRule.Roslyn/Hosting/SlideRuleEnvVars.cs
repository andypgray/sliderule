namespace SlideRule.Roslyn.Hosting;

/// <summary>
///     Every environment variable the product reads, named once. "What can an operator set" is a question
///     with one answer only if the names live together; spelled at each reader they are an inventory
///     nobody holds, and the prose that documents them (the MCP server instructions, the MSBuild
///     quarantine's dragons note) cites names it cannot check.
/// </summary>
/// <remarks>
///     The last six are not ours: they belong to the host, Claude Code or the operating system, and are read
///     to fit in with it rather than to configure anything of SlideRule's.
/// </remarks>
internal static class SlideRuleEnvVars
{
    /// <summary>Explicit solution-file path; bypasses CWD-walk discovery.</summary>
    internal const string SolutionPath = "SLIDERULE_SOLUTION_PATH";

    /// <summary>Overrides the auto-selected Visual Studio install root for MSBuild registration.</summary>
    internal const string VsInstallPath = "SLIDERULE_VS_INSTALL_PATH";

    /// <summary>Overrides the persisted extraction cache's root directory; also the test-isolation knob.</summary>
    internal const string CacheDirectory = "SLIDERULE_CACHE_DIR";

    /// <summary>
    ///     Minutes of tool inactivity after which the MCP server exits; <c>0</c> is the documented opt-out,
    ///     and anything unparseable falls back to the default rather than disabling the watchdog.
    /// </summary>
    internal const string IdleTimeoutMinutes = "SLIDERULE_IDLE_TIMEOUT_MINUTES";

    /// <summary><c>true</c> opts the MCP server out of exiting when the process that launched it dies.</summary>
    internal const string DisableParentWatch = "SLIDERULE_DISABLE_PARENT_WATCH";

    /// <summary>
    ///     <c>true</c> makes the MCP server acquire the solution cold on every call instead of holding one
    ///     warm workspace for its lifetime.
    /// </summary>
    internal const string DisableWarmWorkspace = "SLIDERULE_DISABLE_WARM_WORKSPACE";

    /// <summary>The MCP server's minimum file-log level, in either Serilog or Microsoft level names.</summary>
    internal const string LogLevel = "SLIDERULE_LOG_LEVEL";

    /// <summary>
    ///     Where <c>sliderule hook</c> keeps its per-session state, in place of the temp directory; also the
    ///     test-isolation knob for the hook.
    /// </summary>
    internal const string HookStateDirectory = "SLIDERULE_HOOK_STATE_DIR";

    /// <summary>
    ///     The launching agent session's id, set by Claude Code. Tagged onto every log line so concurrent
    ///     servers sharing one daily file can be told apart and correlated with the session that started them.
    /// </summary>
    internal const string ClaudeCodeSessionId = "CLAUDE_CODE_SESSION_ID";

    /// <summary>
    ///     The project's root directory, set by Claude Code for every hook it fires. <c>sliderule hook</c> checks
    ///     that tree unless the session is in another working tree of the same repository.
    /// </summary>
    internal const string ClaudeProjectDirectory = "CLAUDE_PROJECT_DIR";

    /// <summary>The MCP client's per-response token budget, read to size the response cap.</summary>
    internal const string MaxMcpOutputTokens = "MAX_MCP_OUTPUT_TOKENS";

    /// <summary>Claude Code's configuration directory, read to find the user's own <c>CLAUDE.md</c> there.</summary>
    internal const string ClaudeConfigDirectory = "CLAUDE_CONFIG_DIR";

    /// <summary>The home directory on Windows, read to find the user's own <c>.claude/CLAUDE.md</c>.</summary>
    internal const string UserProfile = "USERPROFILE";

    /// <summary>The home directory off Windows, read to find the user's own <c>.claude/CLAUDE.md</c>.</summary>
    internal const string Home = "HOME";
}
