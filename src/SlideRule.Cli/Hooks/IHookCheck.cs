using SlideRule.Cli.Verbs;

namespace SlideRule.Cli.Hooks;

/// <summary>
///     The check <c>sliderule hook</c> spends when it decides one is due: a <c>check</c> run rendered for the
///     hook, answered with what that run exited and wrote.
/// </summary>
/// <remarks>A seam so the policy around it can be driven without a workspace.</remarks>
internal interface IHookCheck
{
    /// <summary>Runs the check <paramref name="request" /> describes and captures its outcome.</summary>
    Task<HookCheckResult> RunAsync(CheckRequest request, CancellationToken ct);
}

/// <summary>A check run's exit code, one of <see cref="ExitCodes" />, and what it wrote, each channel apart.</summary>
internal sealed record HookCheckResult(int ExitCode, string Output, string Error);
