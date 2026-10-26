using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Roslyn;

/// <summary>
///     A loaded MSBuild solution together with the workspace that produced it. Dispose it to release the
///     workspace and its out-of-process build host; the <see cref="Solution" /> stays readable afterwards.
/// </summary>
/// <remarks>
///     How completely the solution loaded is the project lists of the value <see cref="LoadDiagnosticsWith" />
///     builds, each ordinal-sorted and meaning what its namesake on <see cref="WorkspaceDiagnostics" /> means.
/// </remarks>
internal sealed class LoadedSolution : IDisposable
{
    private readonly MSBuildWorkspace _workspace;
    private readonly WorkspaceDiagnostics _loadVerdict;

    internal LoadedSolution(
        MSBuildWorkspace workspace, Solution solution,
        IReadOnlyDictionary<ProjectId, string> targetFrameworks,
        ProjectLoadReport report,
        IReadOnlyList<string> restoreFailedProjects)
    {
        _workspace = workspace;
        Solution = solution;
        TargetFrameworks = targetFrameworks;
        _loadVerdict = WorkspaceDiagnostics.OfLoad(
            loadFailures: [],
            failedProjects: report.Failed,
            uncheckedProjects: report.Unchecked,
            restoreFailedProjects: restoreFailedProjects,
            unsupportedProjects: report.Unsupported);
    }

    /// <summary>
    ///     Gets the loaded solution: references that did not resolve have been dropped, and a multi-targeted
    ///     project's several compilations all carry the one project name, told apart by
    ///     <see cref="TargetFrameworks" />.
    /// </summary>
    public Solution Solution { get; }

    /// <summary>
    ///     Gets the target framework each multi-targeted project was loaded for, keyed by
    ///     <see cref="ProjectId" />; empty when every project targets one framework.
    /// </summary>
    public IReadOnlyDictionary<ProjectId, string> TargetFrameworks { get; }

    /// <summary>
    ///     This load's verdict as the one value every surface reads. <paramref name="loadFailures" /> are the
    ///     failure messages the loader wrote to the caller's sink, which never land on this type; the merge's
    ///     notes and multi-targeted projects stay empty, since only extraction produces them.
    /// </summary>
    internal WorkspaceDiagnostics LoadDiagnosticsWith(IReadOnlyList<string> loadFailures)
    {
        return _loadVerdict with { LoadFailures = loadFailures };
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }
}
