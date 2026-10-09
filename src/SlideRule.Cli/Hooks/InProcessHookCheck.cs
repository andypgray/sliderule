using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Verbs;

namespace SlideRule.Cli.Hooks;

/// <summary>
///     The check in this process: the same pipeline, runner and error handler <c>sliderule check</c> goes
///     through, writing into buffers instead of the console.
/// </summary>
/// <param name="hostSource">The solution source the CLI was given, or null for a fresh one-shot workspace.</param>
/// <param name="environment">The environment seam the CLI was given, or null for real process state.</param>
internal sealed class InProcessHookCheck(ISolutionSource? hostSource, IEnvironment? environment) : IHookCheck
{
    /// <inheritdoc />
    public async Task<HookCheckResult> RunAsync(CheckRequest request, CancellationToken ct)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        // The handler every command runs under, reporting to the same writer the run does, so a refusal lands in
        // the buffer with the text and the exit code `sliderule check` would have given it.
        int code = await CommandEntryPoint.RunAsync(
            () => MsBuildGate.RunCheckAsync(request, output, error, hostSource, environment, ct),
            error,
            ct);

        return new HookCheckResult(code, output.ToString(), error.ToString());
    }
}
