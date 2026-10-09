using SlideRule.Cli.Mcp;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     The <see cref="McpServerBinding" /> every MCP suite starts a server from: the solution and spec under
///     test, anchored at the solution's own directory the way <c>McpServerCommand</c> anchors a real server.
/// </summary>
internal static class McpServerBindings
{
    /// <summary>
    ///     A binding to <paramref name="solution" /> and <paramref name="spec" />, working-directoried at the
    ///     solution's directory — or at the process's own when there is no solution to anchor to.
    /// </summary>
    internal static McpServerBinding For(string? solution, string? spec)
    {
        string workingDirectory = solution is null
            ? Directory.GetCurrentDirectory()
            : SolutionPaths.SolutionDirectoryOf(solution);
        return new McpServerBinding(solution, spec, workingDirectory);
    }

    /// <summary>The MyApp fixture solution bound to the clean spec — the pairing with nothing to say.</summary>
    internal static McpServerBinding MyAppWithCleanSpec()
    {
        return For(CliRunner.MyAppSolution, CliRunner.CleanSpecDll);
    }

    /// <summary>The MyApp fixture solution bound to the violated spec — the pairing whose report has sites.</summary>
    internal static McpServerBinding MyAppWithViolatedSpec()
    {
        return For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll);
    }
}
