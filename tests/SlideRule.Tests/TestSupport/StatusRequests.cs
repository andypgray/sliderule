using SlideRule.Cli.Verbs;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The plain <c>status</c> run every <see cref="StatusRunner" /> row starts from: the human burndown, over
///     the cache, with no binlog and workspace diagnostics not allowed. A row that cares about one of those
///     says so with a <c>with</c> clause instead of respelling seven positional arguments, three of them a
///     bare <c>false</c> and one a bare <c>null</c>.
/// </summary>
/// <remarks>
///     The twin of <see cref="CheckRequests" />, and for its reason: what a row is about should be the only
///     thing it spells.
/// </remarks>
internal static class StatusRequests
{
    /// <summary><c>status</c> of <paramref name="solution" /> against <paramref name="spec" />.</summary>
    internal static StatusRequest For(string solution, string? spec, string workingDirectory)
    {
        return new StatusRequest(
            solution, spec, Json: false, workingDirectory, NoCache: false, Binlog: null,
            AllowWorkspaceDiagnostics: false);
    }
}
