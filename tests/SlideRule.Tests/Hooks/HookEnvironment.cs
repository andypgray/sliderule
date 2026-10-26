using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Mcp.TestDoubles;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     The environment a hook run reads: a state directory of the row's own, and a project directory that is
///     whatever the row names or nothing at all, never the real one a test process inherits from a Claude Code
///     session.
/// </summary>
internal static class HookEnvironment
{
    /// <summary>
    ///     A fake environment carrying <paramref name="stateDirectory" /> and, as <c>CLAUDE_PROJECT_DIR</c>,
    ///     <paramref name="projectDirectory" />; a null project directory leaves the variable unset.
    /// </summary>
    internal static FakeEnvironment With(string stateDirectory, string? projectDirectory = null)
    {
        return new FakeEnvironment()
            .SetVariable(SlideRuleEnvVars.HookStateDirectory, stateDirectory)
            .SetVariable(SlideRuleEnvVars.ClaudeProjectDirectory, projectDirectory);
    }
}
