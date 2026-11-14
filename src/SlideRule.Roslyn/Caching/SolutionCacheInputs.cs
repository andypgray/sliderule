using Microsoft.CodeAnalysis;

namespace SlideRule.Roslyn.Caching;

/// <summary>
///     The extraction cache's view of a loaded <see cref="Solution" />: <see cref="SolutionProjects" />'
///     per-project collapse with the one thing that is this cache's own — which documents it tracks.
/// </summary>
/// <remarks>
///     Generated documents under a <see cref="BuildOutputDirectories">build-output directory</see>
///     (<c>*.AssemblyInfo.cs</c>, <c>*.GlobalUsings.g.cs</c>, analyzer and source-generator output) are not
///     tracked: their mtime, and sometimes their bytes, churn on every design-time build, while their content is a
///     function of inputs already fingerprinted (the project file, its assets and the on-disk source), so tracking
///     them would only manufacture false dirties. The exclusion rests on that derivation, never on the output being
///     empty: generator output does declare types (GRAMMAR §4.1). It also matches the cone scan, which prunes the
///     same directories. A source under a <c>bin</c> or <c>obj</c> with no project file beside it is tracked, as the
///     SDK compiles it.
/// </remarks>
internal static class SolutionCacheInputs
{
    internal static IReadOnlyList<ProjectInputs> Collect(Solution solution)
    {
        var buildOutput = new BuildOutputDirectories();
        return SolutionProjects.Collect(
            solution,
            (projectDirectory, documentPath) => !buildOutput.IsUnderBuildOutput(projectDirectory, documentPath));
    }

    /// <summary>
    ///     The loaded projects in other languages, filtered as <see cref="Collect" /> filters a C# project's
    ///     documents, whose edits the cache treats as structural.
    /// </summary>
    internal static IReadOnlyList<ForeignProjectInputs> CollectForeign(Solution solution)
    {
        var buildOutput = new BuildOutputDirectories();
        return SolutionProjects.CollectForeign(
            solution,
            (projectDirectory, documentPath) => !buildOutput.IsUnderBuildOutput(projectDirectory, documentPath));
    }
}
