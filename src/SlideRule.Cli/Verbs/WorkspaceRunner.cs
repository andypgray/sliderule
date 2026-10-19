using SlideRule.Cli.Pipeline;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     What every verb's runner is: a pipeline over one <see cref="ISolutionSource" />, injected so a host that
///     already holds a solution can serve the run. Nothing else is shared here: what each verb does with the
///     acquired codebase is the verb.
/// </summary>
internal abstract class WorkspaceRunner(ISolutionSource source)
{
    /// <summary>The seam this run acquires its solution — and its spec model — through.</summary>
    protected ISolutionSource SolutionSource { get; } = source;
}
