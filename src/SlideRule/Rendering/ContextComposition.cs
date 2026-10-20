namespace SlideRule.Rendering;

/// <summary>
///     The whole result of composing a solution's agent-context files. Nothing is printed: the caller
///     decides whether the warnings reach anyone.
/// </summary>
internal sealed class ContextComposition
{
    internal ContextComposition(IReadOnlyList<ContextFile> files, IReadOnlyList<string> warnings)
    {
        Files = files;
        Warnings = warnings;
    }

    /// <summary>
    ///     Gets the composed files: the root file first, then the files holding scoped cards, in placement order.
    /// </summary>
    public IReadOnlyList<ContextFile> Files { get; }

    /// <summary>Gets why each unplaced card was skipped, in placement order.</summary>
    public IReadOnlyList<string> Warnings { get; }
}
