using SlideRule.Checking;
using SlideRule.Cli.Verbs;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     <see cref="BaselineRequest" />s for the runner seams that take a report or a model rather than load a workspace
///     (<see cref="BaselineRunner.AddEntry" />, <see cref="BaselineRunner.ApplyFiles" />,
///     <see cref="BaselineRunner.Rename" />): no solution, no
///     spec, the cache on and workspace diagnostics not allowed, because none of those reach the seam. One
///     factory per subcommand, so a row names the mode it is about. A run that does load a workspace binds the
///     solution side (<c>Solution</c>, <c>Spec</c>, <c>NoCache</c>, <c>AllowWorkspaceDiagnostics</c>) with a
///     <c>with</c> clause.
/// </summary>
internal static class BaselineRequests
{
    /// <summary><c>baseline add</c> of one edge entry, resolved against <paramref name="workingDirectory" />.</summary>
    internal static BaselineRequest Add(string ruleId, string because, string source, string target, string workingDirectory)
    {
        var addition = new BaselineAddition(ruleId, because, source, target, Subject: null);
        return Request(BaselineMode.Add, workingDirectory, addition);
    }

    /// <summary><c>baseline init</c> over every ratcheted rule.</summary>
    internal static BaselineRequest Init(string workingDirectory)
    {
        return Request(BaselineMode.Init, workingDirectory);
    }

    /// <summary><c>baseline accept-reductions</c> over every ratcheted rule.</summary>
    internal static BaselineRequest AcceptReductions(string workingDirectory)
    {
        return Request(BaselineMode.AcceptReductions, workingDirectory);
    }

    /// <summary>
    ///     <c>baseline rename</c> of the section filed under <paramref name="from" /> to <paramref name="to" />.
    /// </summary>
    internal static BaselineRequest Rename(string from, string to, string workingDirectory)
    {
        return Request(BaselineMode.Rename, workingDirectory) with { Rename = new BaselineRename(from, to) };
    }

    /// <summary>
    ///     Runs <paramref name="request" /> through <see cref="BaselineRunner.ApplyFiles" /> over
    ///     <paramref name="results" />, resolving baseline paths against <paramref name="solutionDirectory" />,
    ///     and returns what the runner wrote. The exit code is dropped.
    /// </summary>
    internal static string Apply(
        this BaselineRequest request, IEnumerable<RuleResult> results, string solutionDirectory)
    {
        var output = new StringWriter();
        BaselineRunner.ApplyFiles(output, request, results, solutionDirectory);
        return output.ToString();
    }

    private static BaselineRequest Request(
        BaselineMode mode, string workingDirectory, BaselineAddition? addition = null)
    {
        return new BaselineRequest(
            null, null, mode, workingDirectory, NoCache: false, AllowWorkspaceDiagnostics: false, addition);
    }
}
