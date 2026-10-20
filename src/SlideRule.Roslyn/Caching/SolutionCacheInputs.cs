using Microsoft.CodeAnalysis;

namespace SlideRule.Roslyn.Caching;

/// <summary>
///     The extraction cache's view of a loaded <see cref="Solution" />: <see cref="SolutionProjects" />'
///     per-project collapse with the one thing that is this cache's own — which documents it tracks.
/// </summary>
/// <remarks>
///     Generated documents under <c>bin</c>/<c>obj</c> (<c>*.AssemblyInfo.cs</c>, <c>*.GlobalUsings.g.cs</c>,
///     analyzer and source-generator output) are not tracked: their mtime, and sometimes their bytes, churn on
///     every design-time build, while their content is a function of inputs already fingerprinted (the project
///     file, its assets and the on-disk source), so tracking them would only manufacture false dirties. The
///     exclusion rests on that derivation, never on the output being empty: generator output does declare types
///     (GRAMMAR §4.1). It also matches the cone scan, which skips <c>bin</c>/<c>obj</c> too.
/// </remarks>
internal static class SolutionCacheInputs
{
    internal static IReadOnlyList<ProjectInputs> Collect(Solution solution)
    {
        return SolutionProjects.Collect(
            solution,
            static (projectDirectory, documentPath) =>
                !BuildOutputDirectories.IsUnderBuildOutput(projectDirectory, documentPath));
    }
}
