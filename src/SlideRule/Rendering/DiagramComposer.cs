using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Internal;

namespace SlideRule.Rendering;

/// <summary>
///     Composes the whole body <c>sliderule render --diagram</c> writes: the codebase survey
///     (<see cref="GraphDiagramRenderer" />), a blank line, and then the architecture the spec declares
///     (<see cref="LawDiagramRenderer" />), each with its own caption and its own fence.
/// </summary>
// The single seam the `render` command and the committed-artifact drift gate both go through, for the
// reason the comment on ContextFileComposer gives.
internal static class DiagramComposer
{
    /// <summary>
    ///     <paramref name="scope" /> filters the survey alone: the second fence is drawn from the spec rather
    ///     than from the project graph, so a narrowed survey can leave that fence naming places the survey
    ///     above it dropped.
    /// </summary>
    public static string Compose(
        GraphSummary summary, string solutionName, ArchitectureModel model, string specName, DiagramScope? scope = null)
    {
        Guard.NotNull(summary, nameof(summary));
        Guard.NotNull(model, nameof(model));

        return GraphDiagramRenderer.Block(summary, solutionName, scope)
               + "\n\n"
               + LawDiagramRenderer.Block(model, specName);
    }
}
