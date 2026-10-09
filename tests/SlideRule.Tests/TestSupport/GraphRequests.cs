using SlideRule.Cli.Rendering;
using SlideRule.Cli.Verbs;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The plain <c>graph</c> run every <see cref="GraphRunner" /> row starts from: the human survey at full
///     grain, over the cache, with no binlog, no project filter and workspace diagnostics not allowed — and no
///     spec, because the survey is a property of the codebase. A row that cares about one of those says so with
///     a <c>with</c> clause instead of respelling eight positional arguments, three of them a bare
///     <c>false</c> and two a bare <c>null</c>.
/// </summary>
/// <remarks>
///     The twin of <see cref="CheckRequests" />, and for its reason: what a row is about should be the only
///     thing it spells.
/// </remarks>
internal static class GraphRequests
{
    /// <summary><c>graph</c> of <paramref name="solution" />.</summary>
    internal static GraphRequest For(string solution, string workingDirectory)
    {
        return new GraphRequest(
            solution, Json: false, workingDirectory, NoCache: false, Binlog: null,
            AllowWorkspaceDiagnostics: false, DocumentGrain.Full, Projects: null);
    }
}
