using SlideRule.Cli.Verbs;

namespace SlideRule.Cli.Hooks;

/// <summary>
///     The check <c>sliderule hook</c> spends when it decides one is due: a <c>check</c> run rendered for the
///     hook, answered with what that run exited and wrote.
/// </summary>
/// <remarks>
///     A seam so the policy around it can be driven without a workspace. The hook's tests hand it a canned exit
///     code and report, and hold the policy to what it does with each.
/// </remarks>
internal interface IHookCheck
{
    /// <summary>Runs the check <paramref name="request" /> describes and captures its outcome.</summary>
    /// <param name="request">The check to run: hook mode, the event it answers, the tree it is about.</param>
    /// <param name="ct">The hook's own cancellation.</param>
    Task<HookCheckResult> RunAsync(CheckRequest request, CancellationToken ct);
}

/// <summary>What a check run exited with and wrote, each channel apart.</summary>
/// <param name="ExitCode">The check's own exit code, one of <see cref="ExitCodes" />.</param>
/// <param name="Output">What the check wrote to stdout: under hook mode, the hook document or the report.</param>
/// <param name="Error">What it wrote to stderr: notices, and the refusal a failed run ends with.</param>
internal sealed record HookCheckResult(int ExitCode, string Output, string Error);
