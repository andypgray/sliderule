using SlideRule.Cli.Hooks;
using SlideRule.Cli.Verbs;

namespace SlideRule.Tests.Hooks;

/// <summary>One row of the exit-code contract <c>sliderule hook</c> keeps.</summary>
/// <param name="CheckExit">The code the check exits with, as <c>sliderule check</c> would.</param>
/// <param name="HookExit">The code the hook exits with for it.</param>
/// <param name="Outcome">What becomes of the stop, and where the check's output goes.</param>
internal sealed record HookExitContract(int CheckExit, int HookExit, HookOutcome Outcome)
{
    /// <summary>
    ///     The rows <c>hooks/README.md</c> states in its exit-code table, and the rows <see cref="HookRunnerTests" />
    ///     drives the verb through. Both columns are the product's own constants, the CLI's on the left and the
    ///     hook's on the right.
    /// </summary>
    internal static IReadOnlyList<HookExitContract> Rows { get; } =
    [
        new(ExitCodes.Clean, HookExitCodes.Proceed, HookOutcome.StopStands),
        new(ExitCodes.Violations, HookExitCodes.Block, HookOutcome.StopRefused),
        new(ExitCodes.Error, HookExitCodes.Report, HookOutcome.ConfigError)
    ];
}

/// <summary>What Claude Code does with a stop, given the code the hook exits with.</summary>
internal enum HookOutcome
{
    /// <summary>The stop stands, and whatever the check wrote reaches Claude Code on stdout.</summary>
    StopStands,

    /// <summary>The stop is refused, and the report on stderr is the agent's reason to keep working.</summary>
    StopRefused,

    /// <summary>The user sees a configuration problem, reported on stderr under the hook's config-error line.</summary>
    ConfigError
}
