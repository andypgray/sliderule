using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Verbs;

namespace SlideRule.Cli.Hooks;

/// <summary>
///     The check in this process: the same pipeline, runner and error handler <c>sliderule check</c> goes
///     through, over the solution source and environment the CLI was given, writing into buffers instead of the
///     console.
/// </summary>
internal sealed class InProcessHookCheck(ISolutionSource hostSource, IEnvironment environment) : IHookCheck
{
    /// <inheritdoc />
    public async Task<HookCheckResult> RunAsync(CheckRequest request, CancellationToken ct)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        // The handler every command runs under, reporting to the same writer the run does, so a refusal lands in
        // the buffer with the text and the exit code `sliderule check` would have given it.
        int code = await CommandEntryPoint.RunAsync(
            () => MsBuildGate.RunHookCheckAsync(request, output, error, hostSource, environment, ct),
            error,
            ct);

        return new HookCheckResult(code, output.ToString(), error.ToString());
    }
}
