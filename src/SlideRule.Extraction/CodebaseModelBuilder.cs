using SlideRule.Codebase;
using SlideRule.Extraction.Fragments;

namespace SlideRule.Extraction;

/// <summary>
///     Builds a <see cref="CodebaseModel" /> from a set of compilations: <see cref="FragmentExtractor" />
///     extracts each input into a self-contained <see cref="CodebaseFragment" />, and
///     <see cref="FragmentMerger" /> unifies the fragments by fully-qualified name.
/// </summary>
internal static class CodebaseModelBuilder
{
    public static CodebaseModel Build(IReadOnlyList<CompilationInput> inputs, CancellationToken cancellationToken)
    {
        CodebaseFragment[] fragments = FragmentExtractor.ExtractAll(inputs, cancellationToken);

        return FragmentMerger.Merge(fragments);
    }
}
