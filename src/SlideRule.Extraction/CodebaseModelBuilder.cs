using SlideRule.Codebase;
using SlideRule.Extraction.Fragments;

namespace SlideRule.Extraction;

/// <summary>
///     Builds a <see cref="CodebaseModel" /> from a set of compilations by extracting each input into a
///     self-contained <see cref="CodebaseFragment" /> (<see cref="FragmentExtractor" />) and then
///     unifying the fragments by fully-qualified name (<see cref="FragmentMerger" />). This is deliberately
///     the <em>one</em> code path for cold runs, the MSBuild-free fast test path, and cache
///     hits — a hit replays persisted fragments through the same merge — so the cache cannot change results
///     by construction.
/// </summary>
internal static class CodebaseModelBuilder
{
    public static CodebaseModel Build(
        IReadOnlyList<CompilationInput> inputs, IReadOnlyList<ProjectArtifactFacts?>? artifactFacts,
        CancellationToken cancellationToken)
    {
        CodebaseFragment[] fragments = FragmentExtractor.ExtractAll(inputs, artifactFacts, cancellationToken);

        return FragmentMerger.Merge(fragments);
    }
}
