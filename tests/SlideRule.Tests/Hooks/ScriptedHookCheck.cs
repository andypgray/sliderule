using SlideRule.Cli.Hooks;
using SlideRule.Cli.Verbs;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     A hand-rolled <see cref="IHookCheck" /> for the hook's policy tests: it records each check it is asked to run
///     and answers every one with the same exit code and stdout, the way a stub command stands in for the tool,
///     except a check of the tree <paramref name="redTree" /> names, which exits with violations.
/// </summary>
/// <param name="exitCode">The code every check exits with, one of the CLI's own.</param>
/// <param name="output">What every check writes to stdout; empty for a clean check with nothing to say.</param>
/// <param name="redTree">A tree whose check reports violations whatever the others do, or null for none.</param>
/// <param name="error">What every check writes to stderr; empty for none.</param>
internal sealed class ScriptedHookCheck(int exitCode, string output, string? redTree = null, string error = "")
    : IHookCheck
{
    private readonly List<CheckRequest> _requests = [];

    /// <summary>Every check the hook asked for, in order.</summary>
    public IReadOnlyList<CheckRequest> Requests => _requests;

    /// <inheritdoc />
    public Task<HookCheckResult> RunAsync(CheckRequest request, CancellationToken ct)
    {
        _requests.Add(request);
        bool red = redTree is not null && HookRunAssertions.SameDirectory(request.WorkingDirectory, redTree);
        return Task.FromResult(new HookCheckResult(red ? ExitCodes.Violations : exitCode, output, error));
    }
}
