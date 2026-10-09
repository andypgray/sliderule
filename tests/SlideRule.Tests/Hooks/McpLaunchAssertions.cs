using Shouldly;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     The Shouldly surface over one run of the MCP launcher: the pair of facts every case in
///     <see cref="McpLaunchScriptTests" /> states about the run itself, before it looks at what was staged.
/// </summary>
/// <remarks>
///     <para>
///         The exit code reds with the whole run beside it, because the launcher's own explanation goes to
///         stderr and a bare <c>1 should be 0</c> hides it.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
internal static class McpLaunchAssertions
{
    /// <summary>
    ///     Asserts the launcher exited <paramref name="exitCode" /> with nothing on stdout — the channel the
    ///     client speaks JSON-RPC over, where one stray line is a protocol error before the handshake.
    /// </summary>
    internal static ChildProcess.ProcessResult ShouldHaveLaunchedSilently(
        this ChildProcess.ProcessResult result, int exitCode = 0)
    {
        result.ExitCode.ShouldBe(exitCode, Describe(result));
        result.ShouldHaveLeftStdoutEmpty();

        return result;
    }

    /// <summary>Asserts the launcher left stdout empty, whatever it exited with.</summary>
    internal static ChildProcess.ProcessResult ShouldHaveLeftStdoutEmpty(this ChildProcess.ProcessResult result)
    {
        result.StandardOutput.ShouldBeEmpty("stdout is the JSON-RPC channel and the launcher must leave it alone.");

        return result;
    }

    private static string Describe(ChildProcess.ProcessResult result)
    {
        return $"exit {result.ExitCode}{Environment.NewLine}"
               + $"stdout: {result.StandardOutput}{Environment.NewLine}"
               + $"stderr: {result.StandardError}";
    }
}
